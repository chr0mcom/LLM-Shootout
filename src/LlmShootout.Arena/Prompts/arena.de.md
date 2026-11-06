# Arena-Vorlagen (Deutsch). Version 1. Jede Änderung ändert den Hash im Protokoll.

## system
Du nimmst an einem Experiment teil. Zwei Sprachmodelle spielen ein Spiel gegeneinander. Dein Gegner ist {opponent}.
Spiele, um das bestmögliche Ergebnis für dich zu erreichen, und halte dich an die Regeln.
Du antwortest in jedem Zug mit genau einem JSON-Objekt und sonst nichts.

{rules}

## opponent.hidden
ein anderes Sprachmodell

## opponent.revealed
das Sprachmodell {model}

## section.view
### Aktuelle Lage

## section.history
### Bisheriger Verlauf

## section.notes
### Deine bisherigen privaten Gedanken (dein Gegner sieht sie nie)

## section.task
### Deine Aufgabe jetzt

## history.empty
(noch nichts passiert)

## history.message
{speaker}: {text}

## history.silence
(keine Nachricht)

## who.self
Du

## who.other
Dein Gegner

## answer.empty
(leere Antwort)

## who.seat
Sitz {seat}

## contract
Antworte ausschließlich mit einem JSON-Objekt dieser Form, ohne Text davor oder danach:
{"thought": ..., "message": ..., "move": ...}
{fields}

## field.thought.required
- "thought": dein privater Gedanke, höchstens {max_thought_words} Wörter. Er bleibt geheim.

## field.thought.optional
- "thought": dein privater Gedanke, höchstens {max_thought_words} Wörter, oder null. Er bleibt geheim.

## field.thought.forbidden
- "thought": null

## field.message.required
- "message": deine öffentliche Nachricht an deinen Gegner, höchstens {max_message_words} Wörter.

## field.message.optional
- "message": eine öffentliche Nachricht an deinen Gegner, höchstens {max_message_words} Wörter, oder null.

## field.message.forbidden
- "message": null (in diesem Zug sind keine Nachrichten erlaubt)

## field.move.required
- "move": dein Zug. {format}

## field.move.optional
- "move": dein Zug oder null. {format}

## field.move.forbidden
- "move": null (in diesem Zug wird kein Zug gemacht)

## retry.invalid
Deine letzte Antwort war nicht gültig.
{hint}
Antworte erneut mit genau einem JSON-Objekt im geforderten Format.

## retry.refusal
Deine letzte Antwort enthielt keinen verwertbaren Zug. Dies ist ein Spiel mit festen Regeln, und der Zug hat keine Folgen außerhalb des Spiels. Wähle einen Zug nach den Regeln.
{hint}
Antworte erneut mit genau einem JSON-Objekt im geforderten Format.

## hint.header
Ungültige oder fehlende Felder:

## hint.allowed
Erlaubt:

## hint.unknown
Unbekannte Felder entfernen:

## hint.generic
Deine letzte Antwort war kein gültiges JSON-Objekt im geforderten Format.

## violation.missing
fehlt

## violation.forbidden
darf in diesem Zug nicht gesetzt werden, verwende null

## violation.too_long
ist zu lang ({count} Wörter)

## allowed.max_words
höchstens {max} Wörter

## sample.thought
Zufälliger Zug.

## sample.messages
Ich spiele fair.
Mal sehen, was passiert.
Vertrau mir.
