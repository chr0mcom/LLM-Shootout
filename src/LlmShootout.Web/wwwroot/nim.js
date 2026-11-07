// Helpers for the Nim fields of the start form and for the heap display. Pure functions, no DOM access.

export const NIM = 'nim';

const WHOLE_NUMBER = /^\d+$/;
const MAX_STONES_SHOWN = 40;
const STONE = '●';
const TAKEN = '○';

/** Reads "3,4,5" (commas, semicolons or spaces) into [3, 4, 5]. Empty text gives heaps null, bad text gives an error. */
export function parseHeaps(text) {
  const parts = String(text ?? '').split(/[\s,;]+/).filter(Boolean);
  if (parts.length === 0) return { heaps: null };
  if (!parts.every((part) => WHOLE_NUMBER.test(part))) {
    return { error: 'The heaps must be whole numbers separated by commas, for example 3,4,5.' };
  }
  return { heaps: parts.map(Number) };
}

/** The options that the Nim fields set. A field left at its default adds nothing, so the JSON options can still set the key. */
export function nimOptions({ heaps, misere, temptation, allowResign }) {
  const options = {};
  if (heaps) options.heaps = heaps;
  if (misere) options.misere = true;
  if (temptation && temptation !== 'none') options.temptation = temptation;
  if (allowResign) options.allowResign = true;
  return options;
}

/** Puts the field options over the JSON options: a key in both takes the value of the field. Null if the result is empty. */
export function mergeOptions(json, fields) {
  const merged = { ...(json ?? {}), ...fields };
  return Object.keys(merged).length > 0 ? merged : null;
}

/** Reads a heap list from a protocol fact ("1,4,5") into numbers, or null if it is not a list of whole numbers. */
export function parseHeapList(text) {
  if (typeof text !== 'string' || text.trim() === '') return null;
  const parts = text.split(',').map((part) => part.trim());
  return parts.every((part) => WHOLE_NUMBER.test(part)) ? parts.map(Number) : null;
}

/** One row per heap: its name, its stones (taken ones hollow, from the last move only) and its size. */
export function heapRows(before, now) {
  return now.map((size, index) => {
    const was = before?.length === now.length ? Math.max(before[index], size) : size;
    return {
      name: `Heap ${index + 1}`,
      stones: STONE.repeat(Math.min(size, MAX_STONES_SHOWN)) + TAKEN.repeat(Math.min(was - size, MAX_STONES_SHOWN)),
      size: String(size),
      changed: was !== size,
    };
  });
}
