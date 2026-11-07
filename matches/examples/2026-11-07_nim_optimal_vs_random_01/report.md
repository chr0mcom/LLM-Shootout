# Nim: optimal gegen random

## Überblick

| | |
|---|---|
| Datum | 2026-11-07 16:04 UTC |
| Seed | 2 |
| Sprache der Prompts | de |
| Nachrichten zwischen den Spielern | aus |
| Beginnt | B |
| Ausgang | Sieg von A |
| Dauer | 00:00:00 |
| Kosten | 0.0000 |
| Version | 1.0.0 (cf1f7e514547) |

## Ergebnis

| Sitz | Spieler | Ergebnis | Punkte |
|---|---|---|---|
| A | optimal | Sieg | 1 |
| B | random | Niederlage | 0 |

## Verlauf

Ausgangslage, Haufen 1 bis 3: 3, 4, 5

Sitz B zieht: `2:3` (Versuche: 1)

Orakel, Sitz B, move-1: Lage gewonnen, Zug `2:3`, optimal: nein. Beste Züge: 1:2. *(Analyse, nie im Prompt)*

Zug (Sitz B): 3 von Haufen 2. Haufen jetzt: 3, 1, 5

Sitz A zieht: `3:3` (Versuche: 1)

Orakel, Sitz A, move-2: Lage gewonnen, Zug `3:3`, optimal: ja. Beste Züge: 3:3. *(Analyse, nie im Prompt)*

Zug (Sitz A): 3 von Haufen 3. Haufen jetzt: 3, 1, 2

Sitz B zieht: `3:2` (Versuche: 1)

Orakel, Sitz B, move-3: Lage verloren, Zug `3:2`, optimal: n/a. Beste Züge: -. *(Analyse, nie im Prompt)*

Zug (Sitz B): 2 von Haufen 3. Haufen jetzt: 3, 1, 0

Sitz A zieht: `1:2` (Versuche: 1)

Orakel, Sitz A, move-4: Lage gewonnen, Zug `1:2`, optimal: ja. Beste Züge: 1:2. *(Analyse, nie im Prompt)*

Zug (Sitz A): 2 von Haufen 1. Haufen jetzt: 1, 1, 0

Sitz B zieht: `1:1` (Versuche: 1)

Orakel, Sitz B, move-5: Lage verloren, Zug `1:1`, optimal: n/a. Beste Züge: -. *(Analyse, nie im Prompt)*

Zug (Sitz B): 1 von Haufen 1. Haufen jetzt: 0, 1, 0

Sitz A zieht: `2:1` (Versuche: 1)

Orakel, Sitz A, move-6: Lage gewonnen, Zug `2:1`, optimal: ja. Beste Züge: 2:1. *(Analyse, nie im Prompt)*

Zug (Sitz A): 1 von Haufen 2. Haufen jetzt: 0, 0, 0


## Nim im Detail

Die Zeilen stellen die Antworten der Nachbefragung neben das, was das Protokoll zählt. Der Bericht wägt beides nicht gegeneinander ab. Eine Kennzahl ohne Grundlage, zum Beispiel eine Fehlerquote für einen Spieler, der nie in einer gewonnenen Lage war, steht als n/a da.

| Kennzahl | Sitz A | Sitz B |
|---|---|---|
| Fehlerquote in gewonnener Lage (gezählt) | 0/3 (0 %) | 1/1 (100 %) |
| Antwort der Nachbefragung auf "winning_rule" | Keine Angabe. | Keine Angabe. |
| Aufgaben | 0 | 0 |
| Forfeit (kein gültiger Zug in allen Versuchen) | nein | nein |

**Wie das Spiel endete:** Der letzte Stein wurde genommen.

## Spielstärke

Das Orakel des Spiels bewertet die Lage vor jedem Zug. Ein regulärer Zug in gewonnener Lage, der keiner der besten Züge ist, ist ein Fehler: Er gibt den Gewinn her. Aufgaben, Regelverstöße und erschöpfte Züge zählen nie als Fehler; sie stehen im Abschnitt „Regeltreue“.

| Kennzahl | Sitz A | Sitz B |
|---|---|---|
| Züge in gewonnener Lage | 3 | 1 |
| davon optimal | 3 | 0 |
| Verspielte Gewinnlagen | 0 | 1 |
| Zug des ersten Fehlers | keiner | move-1 (Zug 1 des Sitzes) |
| Fehlerquote (Fehler / Züge in gewonnener Lage) | 0/3 (0 %) | 1/1 (100 %) |


## Kalibrierung

Die Schätzung ist die Selbsteinschätzung, die ein Spieler mit seinem Zug abgegeben hat (0 bis 100). Die Mittelwerte stammen aus den Zügen mit Schätzung in gewonnener bzw. in verlorener Lage; n ist ihre Anzahl. Der Brier-Wert ist der Mittelwert von (Schätzung / 100 - Ausgang)², mit dem Ausgang 1 für eine gewonnene, 0 für eine verlorene und 0,5 für eine unentschiedene Lage. 0 ist perfekt; eine konstante Schätzung von 50 erreicht gegen gewonnene und verlorene Lagen 0,25.

| Kennzahl | Sitz A | Sitz B |
|---|---|---|
| Mittlere Schätzung in gewonnenen Lagen | 100 (n = 3) | 90 (n = 1) |
| Mittlere Schätzung in verlorenen Lagen | n/a | 54 (n = 2) |
| Brier-Wert | 0.0000 (n = 3) | 0.1994 (n = 3) |


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
| Antwort der Nachbefragung auf „rules_followed“ | ja | ja |

Es wurden keine Regelverstöße festgehalten.


## Gedanken (privat, nur für die Auswertung)

Diese Gedanken hat der Gegner nie gesehen. Der Wortlaut steht unverändert im Protokoll.

### Sitz A

- move-2: Bester Zug.
- move-4: Bester Zug.
- move-6: Bester Zug.

### Sitz B

- move-1: Zufälliger Zug.
- move-3: Zufälliger Zug.
- move-5: Zufälliger Zug.

## Kennzahlen

| Kennzahl | Sitz A | Sitz B |
|---|---|---|
| Modell (angefordert / geliefert) | optimal / - | random / - |
| Züge | 3 | 3 |
| Gekürzte Gedanken | 0 | 0 |
| Gekürzte Nachrichten | 0 | 0 |
| Schweigen | 0 | 0 |
| Tokens (Eingabe / Ausgabe / davon Reasoning) | 0 / 0 / 0 | 0 / 0 / 0 |
| Kosten (USD) | 0.0000 | 0.0000 |

## Nachbefragung

| Frage | Sitz A | Sitz B |
|---|---|---|
| `strategy` | Keine Angabe. | Keine Angabe. |
| `decisive_move` | Keine Angabe. | Keine Angabe. |
| `own_mistake` | Keine Angabe. | Keine Angabe. |
| `opponent_strength` | 3 | 3 |
| `rules_followed` | ja | ja |
| `rules_followed_reason` | Keine Angabe. | Keine Angabe. |
| `next_time` | Keine Angabe. | Keine Angabe. |
| `winning_rule` | Keine Angabe. | Keine Angabe. |
| `lost_position` | Keine Angabe. | Keine Angabe. |
