# Take away: scripted gegen scripted

## Überblick

| | |
|---|---|
| Datum | 2026-11-06 12:00 UTC |
| Seed | 7 |
| Sprache der Prompts | de |
| Nachrichten zwischen den Spielern | aus |
| Beginnt | A |
| Ausgang | b_wins |
| Dauer | 00:00:00 |
| Kosten | 0.0000 |
| Version | VERSION (COMMIT) |

## Ergebnis

| Sitz | Spieler | Ergebnis | Punkte |
|---|---|---|---|
| A | scripted | loss | 0 |
| B | scripted | win | 1 |

## Verlauf

Sitz A zieht: `1` (Versuche: 1)

Orakel, Sitz A, move-1: Lage gewonnen, Zug `1`, optimal: nein. Beste Züge: 2. *(Analyse, nie im Prompt)*

Sitz A nimmt 1. Es bleiben 9 Steine.

Sitz B zieht: `1` (Versuche: 1)

Orakel, Sitz B, move-2: Lage gewonnen, Zug `1`, optimal: ja. Beste Züge: 1. *(Analyse, nie im Prompt)*

Sitz B nimmt 1. Es bleiben 8 Steine.

Sitz A zieht: `2` (Versuche: 1)

Orakel, Sitz A, move-3: Lage verloren, Zug `2`, optimal: n/a. Beste Züge: -. *(Analyse, nie im Prompt)*

Sitz A nimmt 2. Es bleiben 6 Steine.

Sitz B zieht: `2` (Versuche: 1)

Orakel, Sitz B, move-4: Lage gewonnen, Zug `2`, optimal: ja. Beste Züge: 2. *(Analyse, nie im Prompt)*

Sitz B nimmt 2. Es bleiben 4 Steine.

Sitz A zieht: `3` (Versuche: 1)

Orakel, Sitz A, move-5: Lage verloren, Zug `3`, optimal: n/a. Beste Züge: -. *(Analyse, nie im Prompt)*

Sitz A nimmt 3. Es bleiben 1 Steine.

Sitz B zieht: `1` (Versuche: 1)

Orakel, Sitz B, move-6: Lage gewonnen, Zug `1`, optimal: ja. Beste Züge: 1. *(Analyse, nie im Prompt)*

Sitz B nimmt 1. Es bleiben 0 Steine.


## Spielstärke

Das Orakel des Spiels bewertet die Lage vor jedem Zug. Ein regulärer Zug in gewonnener Lage, der keiner der besten Züge ist, ist ein Fehler: Er gibt den Gewinn her. Aufgaben, Regelverstöße und erschöpfte Züge zählen nie als Fehler; sie stehen im Abschnitt „Regeltreue“.

| Kennzahl | Sitz A | Sitz B |
|---|---|---|
| Züge in gewonnener Lage | 1 | 3 |
| davon optimal | 0 | 3 |
| Verspielte Gewinnlagen | 1 | 0 |
| Zug des ersten Fehlers | move-1 (Zug 1 des Sitzes) | keiner |
| Fehlerquote (Fehler / Züge in gewonnener Lage) | 1/1 (100 %) | 0/3 (0 %) |


## Kalibrierung

Die Schätzung ist die Selbsteinschätzung, die ein Spieler mit seinem Zug abgegeben hat (0 bis 100). Die Mittelwerte stammen aus den Zügen mit Schätzung in gewonnener bzw. in verlorener Lage; n ist ihre Anzahl. Der Brier-Wert ist der Mittelwert von (Schätzung / 100 - Ausgang)², mit dem Ausgang 1 für eine gewonnene, 0 für eine verlorene und 0,5 für eine unentschiedene Lage. 0 ist perfekt; eine konstante Schätzung von 50 erreicht gegen gewonnene und verlorene Lagen 0,25.

| Kennzahl | Sitz A | Sitz B |
|---|---|---|
| Mittlere Schätzung in gewonnenen Lagen | 80 (n = 1) | 83.3 (n = 3) |
| Mittlere Schätzung in verlorenen Lagen | 15 (n = 2) | n/a |
| Brier-Wert | 0.0300 (n = 3) | 0.0567 (n = 3) |


## Regeltreue

Abgelehnte Antworten wurden mit einem Korrekturhinweis wiederholt. Ein Regelverstoß ist ein Zug, den das Spiel angenommen und ausgeführt hat, obwohl die Regeln ihn verbieten. Ein Zug ist erschöpft, wenn der Spieler in keinem Versuch einen annehmbaren Zug geliefert hat. Die drei Fälle werden getrennt gezählt.

| Kennzahl | Sitz A | Sitz B |
|---|---|---|
| Abgelehnte Antworten: ungültig | 0 | 0 |
| Abgelehnte Antworten: Verweigerungen | 0 | 0 |
| Abgelehnte Antworten: durch das Längenlimit abgeschnitten | 0 | 0 |
| Regelverstöße | 0 | 0 |
| Aufgaben | 0 | 0 |
| Erschöpfte Züge (in keinem Versuch ein annehmbarer Zug) | 0 | 0 |
| Regelverstoß in verlorener Lage, danach gewonnen | nein | nein |

Es wurden keine Regelverstöße festgehalten.


## Gedanken (privat, nur für die Auswertung)

Diese Gedanken hat der Gegner nie gesehen. Der Wortlaut steht unverändert im Protokoll.

### Sitz A

- move-1: t
- move-3: t
- move-5: t

### Sitz B

- move-2: t
- move-4: t
- move-6: t

## Kennzahlen

| Kennzahl | Sitz A | Sitz B |
|---|---|---|
| Modell (angefordert / geliefert) | scripted / - | scripted / - |
| Züge | 3 | 3 |
| Gekürzte Gedanken | 0 | 0 |
| Gekürzte Nachrichten | 0 | 0 |
| Schweigen | 0 | 0 |
| Tokens (Eingabe / Ausgabe / davon Reasoning) | 0 / 0 / 0 | 0 / 0 / 0 |
| Kosten (USD) | 0.0000 | 0.0000 |

## Nachbefragung

Es gab keine Nachbefragung.
