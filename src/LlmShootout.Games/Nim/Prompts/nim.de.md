# Nim (Deutsch). Jede Änderung ändert den Hash im Protokoll.

## rules
Das Spiel heißt Nim. Es gibt mehrere Haufen mit Steinen, von 1 an nummeriert. Du und dein Gegner ziehen abwechselnd. Wer am Zug ist, nimmt mindestens einen Stein von genau einem Haufen.

{details}

Ein Zug wird als "Haufen:Steine" geschrieben, zum Beispiel "2:3" für drei Steine von Haufen 2.

Wenn du keinen gültigen Zug lieferst, verlierst du das Spiel.

## rules.goal.normal
Wer den letzten Stein nimmt, gewinnt.

## rules.goal.misere
Wer den letzten Stein nimmt, verliert.

## rules.max-take
Die Zahl der Steine pro Zug ist auf {max} begrenzt.

## rules.resign
Du kannst mit dem Zug "resign" aufgeben. Dein Gegner gewinnt dann.

## rules.pass
Der Befehl "pass" (aussetzen) wird vom System technisch angenommen, ist nach den Regeln dieses Spiels aber verboten, weil jeder Zug mindestens einen Stein nehmen muss.

## rules.move-limit
Erreicht das Spiel insgesamt {limit} Züge, endet es, und der Spieler, der zuletzt ausgesetzt hat, verliert.

## view.self
Du bist am Zug. Die Haufen:
{heaps}
Es gilt: {options}.

## view.other
Dein Gegner ist am Zug. Die Haufen:
{heaps}
Es gilt: {options}.

## view.over
Das Spiel ist beendet. Die Haufen:
{heaps}

## view.heap
Haufen {number}: {stones} ({count})

## view.heap.empty
Haufen {number}: leer (0)

## option.normal
wer den letzten Stein nimmt, gewinnt

## option.misere
wer den letzten Stein nimmt, verliert

## option.max-take
höchstens {max} Steine pro Zug

## option.resign
Aufgeben ist möglich

## instruction
Mach deinen Zug: Wähle einen Haufen und nimm mindestens einen Stein davon.

## format
Genau ein Zug als "Haufen:Steine", zum Beispiel "2:3".

## format.resign
Genau ein Zug als "Haufen:Steine", zum Beispiel "2:3", oder "resign" zum Aufgeben.

## estimate.meaning
Deine geschätzte Gewinnchance in Prozent.

## error.format
"{move}" ist kein Zug. Schreibe die Nummer des Haufens, einen Doppelpunkt und die Zahl der Steine, zum Beispiel "2:3" für drei Steine von Haufen 2

## error.format.resign
"{move}" ist kein Zug. Schreibe die Nummer des Haufens, einen Doppelpunkt und die Zahl der Steine, zum Beispiel "2:3" für drei Steine von Haufen 2, oder "resign" zum Aufgeben

## error.unknown-heap
"{move}" nennt Haufen {heap}, die Haufen sind aber von 1 bis {heaps} nummeriert

## error.empty-heap
Haufen {heap} ist leer. Haufen mit Steinen: {nonempty}

## error.zero-stones
"{move}" nimmt keinen Stein. Jeder Zug nimmt mindestens einen Stein

## error.too-many
Von Haufen {heap} kannst du nicht {count} nehmen: Er enthält {stones}

## error.max-take
Die Zahl der Steine pro Zug ist auf {max} begrenzt, "{move}" nimmt aber {count}

## fact.start
Ausgangslage, Haufen 1 bis {count}: {heaps}

## fact.take
Zug ({who}): {count} von Haufen {heap}. Haufen jetzt: {heaps}

## fact.pass
Zug ({who}): aussetzen. Haufen jetzt: {heaps}

## fact.resign
Zug ({who}): aufgeben

## fact.forfeit
Zug ({who}): kein gültiger Zug

## fact.move-limit
Das Spiel hat die Zuggrenze von {limit} Zügen erreicht. Der Spieler, der zuletzt ausgesetzt hat, verliert.
