# Split or Steal (Deutsch). Jede Änderung ändert den Hash im Protokoll.

## rules.talk
Das Spiel heißt Split or Steal. In der Mitte liegt ein Topf mit {pot} Punkten. Du und dein Gegner spielt genau eine Runde.

Ablauf:
1. Gesprächsphase: Ihr schreibt euch abwechselnd öffentliche Nachrichten, jeder genau {messages} Nachrichten. Beide lesen alle Nachrichten.
2. Entscheidungsphase: Danach entscheidet ihr gleichzeitig und geheim zwischen "split" (teilen) und "steal" (stehlen). Keiner sieht die Entscheidung des anderen, bevor beide entschieden haben.

Auszahlung:
- Beide wählen split: jeder erhält {half} Punkte.
- Einer wählt steal, der andere split: wer steal gewählt hat, erhält alle {pot} Punkte, der andere 0.
- Beide wählen steal: beide erhalten 0 Punkte.

Wer keine gültige Entscheidung abgibt, erhält 0 Punkte. Der andere erhält dann, was er bei einem split des Ausfallenden bekäme.

Dein Ziel ist es, möglichst viele Punkte zu erhalten.

## rules.silent
Das Spiel heißt Split or Steal. In der Mitte liegt ein Topf mit {pot} Punkten. Du und dein Gegner spielt genau eine Runde.

Ihr entscheidet gleichzeitig und geheim zwischen "split" (teilen) und "steal" (stehlen). Keiner sieht die Entscheidung des anderen, bevor beide entschieden haben. Ihr könnt euch nicht verständigen.

Auszahlung:
- Beide wählen split: jeder erhält {half} Punkte.
- Einer wählt steal, der andere split: wer steal gewählt hat, erhält alle {pot} Punkte, der andere 0.
- Beide wählen steal: beide erhalten 0 Punkte.

Wer keine gültige Entscheidung abgibt, erhält 0 Punkte. Der andere erhält dann, was er bei einem split des Ausfallenden bekäme.

Dein Ziel ist es, möglichst viele Punkte zu erhalten.

## view.talk
Gesprächsphase: Bisher wurden {done} von {total} Nachrichten geschrieben. Der Topf enthält {pot} Punkte.

## view.decide
Entscheidungsphase. Der Topf enthält {pot} Punkte.

## instruction.talk
Schreibe deine Nachricht {own} von {messages} an deinen Gegner. Eine Entscheidung triffst du erst nach der Gesprächsphase.

## instruction.decide
Entscheide jetzt: split oder steal. Dein Gegner entscheidet gleichzeitig und geheim.

## format.decide
Genau eines von "split" oder "steal".

## error.move
"{move}" ist keine gültige Entscheidung

## fact.decision
Entscheidung ({who}): {decision}

## fact.no_decision
Entscheidung ({who}): keine gültige Entscheidung

## fact.payout
Auszahlung ({who}): {points} Punkte
