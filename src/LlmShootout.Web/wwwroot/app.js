const $ = (selector) => document.querySelector(selector);

function h(tag, attrs = {}, ...children) {
  const element = document.createElement(tag);
  for (const [key, value] of Object.entries(attrs)) {
    if (value == null || value === false) continue;
    if (key === 'class') element.className = value;
    else if (key.startsWith('on')) element.addEventListener(key.slice(2), value);
    else element.setAttribute(key, value === true ? '' : value);
  }
  for (const child of children.flat()) {
    if (child == null || child === false) continue;
    element.append(child.nodeType ? child : document.createTextNode(String(child)));
  }
  return element;
}

const words = (text) => String(text ?? '').replaceAll('_', ' ');
const lane = (seat) => (seat === 'A' ? 'a' : 'b');
const money = (usd) => `$${(usd ?? 0).toFixed(usd && usd < 0.01 ? 4 : 2)}`;
const count = (n) => (n ?? 0).toLocaleString('en-US');
const secondsOf = (latency) => {
  const match = /^(?:(\d+)\.)?(\d+):(\d+):(\d+(?:\.\d+)?)$/.exec(latency ?? '');
  return match ? Number(match[1] ?? 0) * 86400 + Number(match[2]) * 3600 + Number(match[3]) * 60 + Number(match[4]) : null;
};
const clock = (seconds) => `${Math.floor(seconds / 60)}:${String(Math.floor(seconds % 60)).padStart(2, '0')}`;
const shortName = (spec) => (spec ?? '').replace(/^[a-z0-9-]+:/i, '').split('/').pop();

async function api(path, options = {}) {
  const response = await fetch(path, { credentials: 'same-origin', ...options });
  if (response.status === 401) {
    location.href = '/login';
    throw new Error('Signed out');
  }
  return response;
}

class Arena {
  constructor() {
    this.timer = null;
    this.reset();
  }

  reset() {
    clearInterval(this.timer);
    this.players = {};
    this.busy = {};
    this.stats = { A: this.blankStats(), B: this.blankStats() };
    this.scores = null;
    this.startedAt = null;
    this.endedAt = null;
    this.live = false;
    this.lastTurn = null;
    this.calls = [];
    $('#rows').replaceChildren();
    $('#calls').replaceChildren();
    $('#callCount').textContent = '';
    $('#banner').hidden = true;
    $('#debrief').hidden = true;
    $('#debriefA').replaceChildren();
    $('#debriefB').replaceChildren();
    $('#files').replaceChildren();
    $('#plateA').replaceChildren();
    $('#plateB').replaceChildren();
    $('#center').replaceChildren();
  }

  blankStats() {
    return { calls: 0, promptTokens: 0, completionTokens: 0, cost: 0, invalid: 0 };
  }

  show() {
    $('#empty').hidden = true;
    $('#stage').hidden = false;
  }

  start(live) {
    this.live = live;
    clearInterval(this.timer);
    if (live) this.timer = setInterval(() => { this.drawCenter(); this.drawPlates(); }, 1000);
  }

  stop() {
    this.live = false;
    this.busy = {};
    clearInterval(this.timer);
    this.drawCenter();
    this.drawPlates();
  }

  setBusy(progress) {
    const offset = Date.now() - new Date(progress.now).getTime();
    this.busy = {};
    for (const seat of ['A', 'B']) {
      if (progress[seat]) this.busy[seat] = new Date(progress[seat]).getTime() + offset;
    }
    this.drawPlates();
  }

  apply(event) {
    this.show();
    const handler = {
      match_started: () => this.onStarted(event),
      model_call: () => this.onCall(event),
      message: () => this.onMessage(event),
      turn: () => this.onTurn(event),
      fact: () => this.onFact(event),
      match_finished: () => this.onFinished(event),
      match_aborted: () => this.onAborted(event),
      debrief: () => this.onDebrief(event),
    }[event.type];
    handler?.();
    this.endedAt = new Date(event.at);
  }

  onStarted(event) {
    this.players = event.players;
    this.startedAt = new Date(event.at);
    this.game = event;
    this.drawPlates();
    this.drawCenter();
  }

  onCall(event) {
    const stats = this.stats[event.seat];
    const info = event.call ?? {};
    stats.calls += 1;
    stats.promptTokens += info.promptTokens ?? 0;
    stats.completionTokens += info.completionTokens ?? 0;
    stats.cost += info.costUsd ?? info.estimatedCostUsd ?? 0;
    this.calls.push(event);
    this.addCallRow(event, this.calls.length - 1);
    if (event.purpose === 'Turn') this.turnMark(event.turnId);
    if (event.purpose !== 'Turn') return;
    if (event.class === 'valid' && event.parsed?.thought) {
      this.lanePut(event.seat, h('p', { class: 'thought' }, h('b', {}, 'thinks'), event.parsed.thought));
    } else if (event.class !== 'valid') {
      stats.invalid += 1;
      const why = event.error ?? event.violations?.join('; ') ?? '';
      this.lanePut(event.seat, h('p', { class: 'note' }, `Try ${event.attempt}: ${words(event.class)}${why ? ` (${why})` : ''}`));
    }
    this.drawPlates();
  }

  onMessage(event) {
    const text = event.silent || event.text == null ? 'stays silent' : event.text;
    const tail = event.truncated ? ' (shortened)' : '';
    this.lanePut(event.seat, h('p', { class: `say${event.silent ? ' silent' : ''}` }, text + tail));
  }

  onTurn(event) {
    if (event.status === 'exhausted') this.lanePut(event.seat, h('span', { class: 'chip' }, 'no valid answer'));
    else if (event.move) this.lanePut(event.seat, h('span', { class: 'chip move' }, `chose ${event.move}`));
  }

  onFact(event) {
    if (event.audience === 'Seat' && event.seat) {
      this.lanePut(event.seat, h('span', { class: 'chip' }, `private: ${event.text}`));
      return;
    }
    const kind = event.audience === 'Analysis' ? 'analysis: ' : '';
    this.midPut(h('span', { class: `chip${event.kind === 'decision' ? ' reveal' : ''}` }, kind + event.text));
  }

  onFinished(event) {
    this.scores = event.result.seats;
    const A = this.scores.A;
    const B = this.scores.B;
    const winner = A.score === B.score ? 'It is a tie' : `Seat ${A.score > B.score ? 'A' : 'B'} comes out ahead`;
    $('#banner').className = 'banner';
    $('#banner').replaceChildren(h('strong', {}, words(event.result.outcome)), ` — ${winner}: ${A.score} to ${B.score}.`);
    $('#banner').hidden = false;
    this.drawPlates();
  }

  onAborted(event) {
    $('#banner').className = 'banner aborted';
    $('#banner').replaceChildren(h('strong', {}, `Stopped: ${words(event.reason)}`), event.detail ? ` — ${event.detail}` : '');
    $('#banner').hidden = false;
  }

  onDebrief(event) {
    $('#debrief').hidden = false;
    const target = $(`#debrief${event.seat}`);
    const list = h('dl');
    for (const [key, value] of Object.entries(event.answers)) list.append(h('dt', {}, words(key)), h('dd', {}, value));
    target.replaceChildren(h('p', { class: 'head' }, `Seat ${event.seat} looks back`));
    if (!event.valid) target.append(h('p', { class: 'warn' }, `Incomplete: ${event.problems.join('; ')}`));
    target.append(list);
  }

  turnMark(turnId) {
    if (this.lastTurn === turnId) return;
    this.lastTurn = turnId;
    $('#rows').append(h('div', { class: 'turn-mark' }, words(turnId)));
  }

  lanePut(seat, node) {
    const rows = $('#rows');
    const last = rows.lastElementChild;
    const same = last?.classList.contains(lane(seat)) && last.dataset.open === '1';
    if (same) {
      last.append(node);
      return;
    }
    const cell = h('div', { class: `cell ${lane(seat)}`, 'data-open': '1' }, node);
    rows.append(cell);
  }

  midPut(node) {
    const rows = $('#rows');
    const last = rows.lastElementChild;
    if (last?.classList.contains('mid')) {
      last.append(node);
      return;
    }
    rows.append(h('div', { class: 'cell mid' }, node));
  }

  closeCells() {
    $('#rows').querySelectorAll('[data-open]').forEach((cell) => cell.removeAttribute('data-open'));
  }

  drawPlates() {
    for (const seat of ['A', 'B']) {
      const player = this.players[seat] ?? {};
      const stats = this.stats[seat];
      const result = this.scores?.[seat];
      const delivered = [...this.calls].reverse().find((c) => c.seat === seat && c.call?.deliveredModel)?.call.deliveredModel;
      const plate = $(`#plate${seat}`);
      plate.className = `plate ${lane(seat)}${result && this.scores && result.score > this.scores[seat === 'A' ? 'B' : 'A'].score ? ' winner' : ''}`;
      plate.replaceChildren(...[
        h('span', { class: 'seat' }, seat),
        h('div', { class: 'who-box' },
          h('p', { class: 'who' }, player.name ?? '…'),
          delivered && delivered !== player.requestedModel ? h('p', { class: 'model' }, `answered by ${delivered}`) : null,
          this.busy[seat] ? h('p', { class: 'busy' }, `answering, ${Math.max(0, Math.round((Date.now() - this.busy[seat]) / 1000))} s`) : null),
        h('p', { class: 'stats' },
          h('span', {}, `${stats.calls} calls`),
          h('span', {}, `${count(stats.promptTokens)} in / ${count(stats.completionTokens)} out`),
          h('span', {}, money(stats.cost)),
          stats.invalid ? h('span', {}, `${stats.invalid} invalid`) : null),
        result ? h('p', { class: 'score' }, String(result.score), h('small', {}, result.label)) : null,
      ].filter(Boolean));
    }
  }

  drawCenter() {
    if (!this.startedAt) return;
    const end = this.live ? new Date() : (this.endedAt ?? new Date());
    const total = (this.stats.A.cost + this.stats.B.cost);
    $('#center').replaceChildren(
      h('span', { class: 'game' }, words(this.game.gameId)),
      h('span', { class: 'clock' }, clock((end - this.startedAt) / 1000)),
      h('span', {}, `seed ${this.game.seed}, ${this.game.language}`),
      h('span', {}, this.game.communication ? 'messages on' : 'messages off'),
      h('span', {}, `first: seat ${this.game.firstSeat}`),
      h('span', {}, money(total)));
  }

  addCallRow(event, index) {
    const info = event.call ?? {};
    const seconds = secondsOf(info.latency);
    const result = event.class;
    $('#calls').append(h('tr', {},
      h('td', {}, words(event.turnId)),
      h('td', { class: `seat-${lane(event.seat)}` }, event.seat),
      h('td', {}, String(event.attempt)),
      h('td', { class: result === 'valid' ? 'good' : 'bad' }, words(result)),
      h('td', { class: 'num' }, count(info.promptTokens)),
      h('td', { class: 'num' }, count(info.completionTokens)),
      h('td', { class: 'num' }, count(info.reasoningTokens)),
      h('td', { class: 'num' }, seconds == null ? '' : seconds.toFixed(1)),
      h('td', { class: 'num' }, (info.costUsd ?? info.estimatedCostUsd ?? 0).toFixed(4)),
      h('td', {}, h('button', { type: 'button', onclick: () => openCall(this.calls[index]) }, 'Open'))));
    $('#callCount').textContent = `(${this.calls.length})`;
  }
}

function openCall(event) {
  const body = $('#callBody');
  const section = (title, text) => (text ? [h('h3', {}, title), h('pre', {}, text)] : []);
  $('#callTitle').textContent = `Seat ${event.seat}, ${words(event.turnId)}, try ${event.attempt}`;
  body.replaceChildren(
    ...(event.messages ?? []).flatMap((m) => section(`Prompt (${m.role.toLowerCase()})`, m.text)),
    ...section('Answer', event.rawContent),
    ...section('Reasoning', event.rawReasoning),
    ...section('Problems', [event.error, ...(event.violations ?? [])].filter(Boolean).join('\n')));
  $('#callDialog').showModal();
}

const arena = new Arena();
let stream = null;
let current = null;
let shownMatch = null;

function setRunState(run) {
  current = run;
  const running = run?.status === 'running';
  const state = $('#runstate');
  state.className = running ? 'runstate live' : 'runstate';
  state.textContent = running ? `Match running: ${run.match}` : run ? `Last match: ${run.status}` : '';
  $('#watchLive').hidden = !running;
  $('#startButton').disabled = running;
  $('#cancelButton').hidden = !running;
}

function followLive(run) {
  openToken += 1;
  stream?.close();
  arena.reset();
  arena.start(true);
  shownMatch = run.match;
  markHistory();
  setFiles(run.match);
  stream = new EventSource(`/api/runs/${run.id}/stream`);
  stream.addEventListener('protocol', (message) => {
    const event = JSON.parse(message.data);
    arena.apply(event);
    if (event.type === 'turn') arena.closeCells();
  });
  stream.addEventListener('progress', (message) => arena.setBusy(JSON.parse(message.data)));
  stream.addEventListener('end', async () => {
    stream.close();
    arena.stop();
    setRunState((await (await api('/api/runs/current')).json()).run);
    loadHistory();
  });
  stream.onerror = async () => {
    if (stream.readyState !== EventSource.CLOSED) return;
    stream.close();
    arena.stop();
    setRunState((await (await api('/api/runs/current')).json()).run);
  };
}

function parseLine(line) {
  try {
    return JSON.parse(line);
  } catch {
    return null;
  }
}

let openToken = 0;

async function openMatch(name) {
  const token = ++openToken;
  stream?.close();
  shownMatch = name;
  markHistory();
  const response = await api(`/api/matches/${encodeURIComponent(name)}/events`);
  if (!response.ok || token !== openToken) return;
  const text = await response.text();
  if (token !== openToken) return;
  arena.reset();
  for (const line of text.split('\n')) {
    const event = line.trim() ? parseLine(line) : null;
    if (!event) continue;
    arena.apply(event);
    if (event.type === 'turn') arena.closeCells();
  }
  arena.stop();
  setFiles(name);
  history.replaceState(null, '', `#${encodeURIComponent(name)}`);
}

function setFiles(name) {
  const base = `/api/matches/${encodeURIComponent(name)}`;
  $('#files').replaceChildren(
    h('a', { href: `${base}/export.xlsx`, download: `${name}.xlsx` }, 'Download as Excel'),
    h('a', { href: `${base}/report`, target: '_blank', rel: 'noopener' }, 'Report (text)'),
    h('a', { href: `${base}/events`, target: '_blank', rel: 'noopener' }, 'Protocol (events.jsonl)'));
}

function markHistory() {
  document.querySelectorAll('#history button').forEach((b) => b.setAttribute('aria-current', String(b.dataset.name === shownMatch)));
}

async function loadHistory() {
  const items = await (await api('/api/matches')).json();
  $('#historyEmpty').hidden = items.length > 0;
  $('#history').replaceChildren(...items.map((item) => {
    const detail = item.complete
      ? `${item.aborted ? 'stopped' : words(item.outcome)}${item.scoreA != null ? `, ${item.scoreA} : ${item.scoreB}` : ''}`
      : 'running or incomplete';
    return h('li', {}, h('button', { type: 'button', 'data-name': item.name, onclick: () => openMatch(item.name) },
      h('span', { class: 'versus' }, h('span', { class: 'va' }, shortName(item.playerA) || 'A'), ' vs ', h('span', { class: 'vb' }, shortName(item.playerB) || 'B')),
      h('span', { class: 'detail' }, `${new Date(item.at).toLocaleString('en-GB', { dateStyle: 'medium', timeStyle: 'short' })}`),
      h('span', { class: 'detail' }, detail)));
  }));
  markHistory();
}

let games = [];

function applyGameDefaults() {
  const game = games.find((g) => g.id === $('#game').value);
  if (game) $('#start').elements.communication.checked = game.defaultCommunication;
}

async function loadConfig() {
  const config = await (await api('/api/config')).json();
  games = config.games;
  $('#game').replaceChildren(...games.map((g) => h('option', { value: g.id }, g.name)));
  applyGameDefaults();
  $('#players').replaceChildren(...config.players.map((p) => h('option', { value: p })));
  $('#maxCost').value = config.defaultMaxCostUsd;
  setRunState(config.current);
  return config;
}

async function startMatch(form) {
  const error = $('#formError');
  error.hidden = true;
  const data = new FormData(form);
  const request = {
    game: data.get('game'),
    a: data.get('a')?.trim(),
    b: data.get('b')?.trim(),
    language: data.get('language'),
    communication: data.get('communication') === 'on',
    debrief: data.get('debrief') === 'on',
    firstSeat: data.get('firstSeat') || null,
    seed: data.get('seed') ? Number(data.get('seed')) : null,
    maxCostUsd: data.get('maxCostUsd') ? Number(String(data.get('maxCostUsd')).replace(',', '.')) : null,
  };
  const button = $('#startButton');
  button.disabled = true;
  let response;
  try {
    response = await api('/api/runs', { method: 'POST', headers: { 'Content-Type': 'application/json' }, body: JSON.stringify(request) });
  } catch {
    button.disabled = false;
    return;
  }
  const isJson = response.headers.get('content-type')?.includes('json');
  const body = isJson ? await response.json() : {};
  if (!response.ok) {
    button.disabled = false;
    error.textContent = body.error ?? `The match could not be started (HTTP ${response.status}).`;
    error.hidden = false;
    return;
  }
  setRunState(body);
  history.replaceState(null, '', location.pathname);
  followLive(body);
  loadHistory();
}

function safeDecode(text) {
  try {
    return decodeURIComponent(text);
  } catch {
    return '';
  }
}

async function main() {
  const config = await loadConfig();
  await loadHistory();
  $('#game').addEventListener('change', applyGameDefaults);
  $('#start').addEventListener('submit', (e) => { e.preventDefault(); startMatch(e.target); });
  $('#cancelButton').addEventListener('click', async () => { if (current) await api(`/api/runs/${current.id}/cancel`, { method: 'POST' }); });
  const wanted = safeDecode(location.hash.slice(1));
  $('#watchLive').addEventListener('click', () => current && followLive(current));
  if (config.current?.status === 'running') followLive(config.current);
  else if (wanted) await openMatch(wanted);
  else if ($('#history button')) await openMatch($('#history button').dataset.name);
}

main();
