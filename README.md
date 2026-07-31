# ELW-Meteo

Desktop-Anwendung für den Einsatzleitwagen: Uhrzeit, taktische Zeit und die
meteorologische Lage an der eigenen Position — plus eine Kartenansicht mit
Regenradar, Nowcast und DWD-Fachkarten und die Radarseiten der Anbieter als
eingebettete Browseransicht.

Zwei Ausgaben aus einem Kern:

- **Windows** — WPF, mit eingebettetem Browser für Karte und Web-Radar
- **Linux, macOS und Windows** — Avalonia, dieselben Registerkarten; Karte und
  Web-Radar öffnen im Standardbrowser

C# / .NET 8.

---

## Was die Anwendung zeigt

### Registerkarte 1 — Uhr

Die Startansicht. Nichts als die Zeit, groß genug, um sie von der anderen Fahrzeugseite abzulesen:
Ortszeit, Datum und Zeitzone, darunter taktische Zeit und UTC nebeneinander,
sowie eine schmale Zeile mit Sonnenauf-/-untergang, Temperatur und Wind. Die
Darstellung skaliert mit der Fenstergröße, füllt also auch einen großen
Monitor komplett aus.

### Registerkarte 2 — Lage & Wetter

**Kopfzeile (immer sichtbar)**

| Anzeige | Format | Zweck |
|---|---|---|
| Ortszeit | `13:47:12`, Wochentag, MEZ/MESZ | Wanduhr |
| Taktische Zeit | `271347BJUL26` | Date-Time-Group nach NATO/BOS |
| Taktische Zeit Zulu | `271147ZJUL26` | dieselbe Zeit in UTC |
| UTC | `11:47:12` | für überregionale Abstimmung |
| Windentwicklung | `dreht rechtsdrehend → NW` | Winddreher und Böenspitze der nächsten 6 h, dauerhaft im Blick |

Die taktische Zeit folgt dem Muster `TTHHMM<Z>MMMJJ` — Tag, Stunde, Minute,
militärischer Zonenbuchstabe, englischer Monat, Jahr. Deutschland liegt im
Winter in Zone **A** (UTC+1), im Sommer in Zone **B** (UTC+2); **Z** ist UTC.
Der Buchstabe **J** ist für „Ortszeit des Beobachters“ reserviert und wird in
der Zonenfolge übersprungen — die Anwendung bildet das korrekt ab.

**Messwerte an der aktuellen Position**

Temperatur, gefühlte Temperatur, Luftfeuchte, Taupunkt, Luftdruck (NN),
Bewölkung, Sichtweite, Niederschlag, CAPE (Gewitterenergie) und die konvektive
Wolkenbasis.

**Wind** mit Kompassrose. Der Pfeil zeigt in die *Ausbreitungsrichtung*, also
dorthin, wohin die Luft strömt — nicht dorthin, woher der Wind kommt.
Dazu Windgeschwindigkeit in km/h und m/s, Beaufort-Stufe mit deutscher
Bezeichnung und die Böenspitze.

**Ausbreitung (Gefahrstoff / Rauch)**

- Ausbreitungsrichtung als Kompasspunkt und Gradzahl
- **Ausbreitungsklasse** nach Klug/Manier (`I` … `V`, VDI 3782) *und*
  Pasquill (`A` … `F`), berechnet aus Windgeschwindigkeit, Bewölkungsgrad und
  Sonnenstand
- Taktischer Klartexthinweis je Klasse — bei stabiler Schichtung z. B. der
  Hinweis auf bodennah kriechende schwere Gase in Senken und Kellern
- Richtwert für den Gefahrenbereich plus Transportzeit der Fahne

**Belastung der Einsatzkräfte**

WBGT (Schattenwert) als Maß für die Wärmebelastung unter PSA,
Feuchtkugeltemperatur, absolute Feuchte und der Ångström-Index als
Screening-Wert für Vegetationsbrandgefahr.

**Tageslicht & Mond**

Sonnenauf- und -untergang, bürgerliche Dämmerung, Tageslänge, aktueller
Sonnenstand (Höhe/Azimut) sowie Mondphase und Beleuchtungsgrad — relevant für
Ausleuchtung und Suchmaßnahmen. Alles lokal gerechnet (NOAA-Algorithmus), also
auch ohne Netz verfügbar.

**Einsatzhinweise** — automatisch abgeleitete Klartextmeldungen, nach Schwere
sortiert. Unter anderem:

- Böen an der Einsatzgrenze von Hubrettungsfahrzeugen
- Sturm ab 8 Bft
- Windstille (keine verlässliche Ausbreitungsrichtung → rundum absperren)
- stabile Schichtung im Gefahrstoffeinsatz
- CAPE- und Blitzpotenzial
- Hitzebelastung unter Atemschutz
- Glätte- und Frostgefahr durch Löschwasser
- eingeschränkte Sichtweite
- Vegetationsbrandgefahr
- Sonnenuntergang binnen einer Stunde
- Starkregen im Nowcast

**Messwerte der nächsten DWD-Station.** Kein Modellwert, sondern das, was ein
Instrument tatsächlich aufgezeichnet hat: Station, Entfernung, Alter der Messung
sowie Temperatur, Wind, Feuchte und Druck. Weicht das Modell um mehr als 1,5 K
von der Station ab, wird das ausdrücklich gemeldet — meist ein Hinweis auf
Geländeeinfluss oder eine Inversion. Ist die nächste Station zu weit entfernt,
sagt die Anwendung das dazu, statt den Wert als lokale Messung auszugeben.

**DWD-Radar am Standort** in 5-Minuten-Schritten: der RADOLAN-Wert genau an der
Einsatzstelle, gefolgt von der RV-Extrapolation des DWD. Kräftige Balken sind
Messung, blasse Vorhersage. Dazu die Klartextaussage, ab wann es laut Radar
regnet — ein Zahlenwert statt eines Bildes, das man interpretieren muss.

**Amtliche DWD-Warnungen** für die Gemeinde an der aktuellen Position, mit
Warnstufe, Gültigkeit, Beschreibung und Verhaltenshinweis. Bezogen aus dem
CAP-Feed des DWD — derselben Quelle, die auch hinter der WarnWetter-App liegt;
schlägt die fehl, übernimmt der DWD-GeoServer als Ausweichweg. Welche Quelle
geliefert hat, steht unter der Zusammenfassung.

**Nowcast-Streifen** — Niederschlag der nächsten drei Stunden in
15-Minuten-Schritten als Balken, mit Klartextzusammenfassung („Niederschlag
beginnt in ca. 30 min“).

**Dokumentation** — zwei Schaltflächen kopieren fertige Textblöcke:
eine vollständige Wettermeldung für das Einsatztagebuch und eine Kurzfassung
zum Absetzen über Funk. Zusätzlich wird jeder Abruf als CSV-Zeile
protokolliert (Semikolon-getrennt, deutsche Dezimalkommas — öffnet in Excel
ohne Importassistent).

### Registerkarte 3 — Karten & Radar

Leaflet-Karte in einem WebView2-Steuerelement.

**Regenradar mit Zeitleiste.** Rund zwei Stunden gemessene Radarkomposite plus
30 Minuten Nowcast in 10-Minuten-Schritten. Ab einer einstellbaren Zoomstufe
(Vorgabe 11) blendet sich das Radar aus und sagt das an: Komposite haben rund
einen Kilometer je Bildpunkt, näher herangezoomt wird nur noch hochskaliert.

Dieselbe Zoomgrenze gilt layerweise für alle grob gerasterten Produkte —
DWD-Niederschlagsradar, Radarvorhersage, Waldbrand- und Graslandfeuerindex,
gefühlte Temperatur und Windböen. Überschrittene Layer werden ausgeblendet und
oben in der Karte namentlich genannt. Das fängt auch den Fall ab, in dem ein
Server die Fehlermeldung als gültiges Bild ausliefert — eine Fehlerkachel, die
kein Ladefehler-Handler bemerken kann. Abspielen, Einzelschritt, Sprung
auf „jetzt“, einstellbare Deckkraft. Beim Bildwechsel wird das neue Bild erst
eingeblendet, wenn seine Kacheln geladen sind — kein Flackern.

**Radarquelle wählbar.** Vier Quellen stehen zur Auswahl:

| Quelle | Zeitachse | Eigenschaft |
|---|---|---|
| **DWD-Radar mit Vorhersage (WN)** | ✔ 5-Min-Schritte | amtliches Komposit **plus zwei Stunden Extrapolation**, alle fünf Minuten aktualisiert |
| RainViewer | ✔ 10-Min-Schritte | weltweit, rund 2 h Messung plus 30 min Nowcast |
| DWD-Niederschlagsradar | — | amtliches RADOLAN-Komposit als Momentaufnahme |
| DWD-Radarvorhersage (FX) | ✔ | Extrapolation über zwei Stunden |
| OpenWeatherMap | — | weltweit, benötigt einen eigenen kostenlosen Schlüssel |

**Der amtliche Weg zur Zeitachse.** Die DWD-Layer veröffentlichen im
GetCapabilities-Dokument eine **TIME-Dimension** — die Liste der Zeitpunkte, für
die der Server ein Bild rendern kann. Die Anwendung liest diese Liste aus und
macht daraus die Zeitleiste; jedes Einzelbild ist dann eine GetMap-Anfrage mit
`TIME=`. Damit animiert das amtliche Produkt genauso wie ein Kacheldienst — in
5-Minuten-Schritten und damit feiner als jeder davon.

Weil die Zeitpunkte vom Server kommen statt aus einer festen Annahme, stimmt die
Zeitleiste automatisch, auch wenn der DWD Taktung oder Vorhersagelänge ändert.
Die Layernamen werden dabei ebenfalls aufgelöst: Jede Quelle nennt mehrere
Kandidaten, und genommen wird der, den der Server tatsächlich anbietet.

Zwei unabhängige Produkte nebeneinander sind operativ wertvoll: Sieht das
Radarbild seltsam aus, lässt sich das durch Vergleich klären, statt einer
einzelnen Quelle glauben zu müssen. Die Oberfläche sagt an, wenn eine Quelle
keinen Zeitverlauf liefert oder der Schlüssel fehlt.

Farbschema der Radarbilder ist wählbar (neun RainViewer-Rampen), Schnee lässt
sich getrennt einfärben und die Kantenglättung abschalten.

**Satellitenbild (Infrarot).** Wolkenoberflächen-Temperaturen, synchron zur
Radar-Zeitleiste. Zeigt das Wolkenfeld auch nachts und außerhalb der
Radarreichweite, wo das Niederschlagsradar nichts liefert.

**Windfeld.** Ein Gitter aus Windpfeilen rund um die Einsatzstelle, in einer
einzigen Abfrage geholt. Größe (3×3 bis 9×9) und Punktabstand (0,5–10 km) sind
einstellbar; Länge und Farbe der Pfeile skalieren mit der Windgeschwindigkeit,
der Tooltip zeigt Richtung, Geschwindigkeit und Böen. Weicht die Strömung im
Umfeld stark ab, meldet die Anwendung die Richtungsspreizung und weist darauf
hin, dass das Gelände die Ausbreitung steuert und der Kegel aus dem einzelnen
Messwert am Fahrzeug zu kurz greift.

**Windanimation im Stil von Windy.** Partikel treiben über die Karte, gesteuert
vom interpolierten Windgitter; die Spuren zeichnen das Strömungsbild, Farbe und
Länge folgen der Windgeschwindigkeit. Eine Legende ordnet die Farben Bft-Stufen
zu. Die Partikeldichte halbiert sich mit jeder Zoomstufe und die Schrittweite
je Bild ist gedeckelt, damit das Bild nah herangezoomt lesbar bleibt statt zu
verschmieren; ab einer einstellbaren Zoomstufe (Vorgabe 13) schaltet sich die
Animation ganz ab, weil dann der ganze Ausschnitt in einer Gitterzelle liegt
und alle Partikel denselben Vektor tragen. Unabhängig davon lassen sich die
diskreten Windpfeile ein- und ausschalten.

**Grundkarten**

| Gruppe | Karten |
|---|---|
| Amtlich (BKG) | basemap.de (Farbe und Grau), TopPlusOpen |
| Karte | OpenStreetMap, OpenStreetMap.de, OSM Humanitarian, OpenTopoMap (Höhenlinien), CyclOSM (Wirtschaftswege), Dunkel (nachttauglich), Luftbild |

basemap.de und TopPlusOpen sind die amtlichen Karten der deutschen
Vermessungsverwaltungen bzw. des BKG — dieselbe Grundlage, die in vielen
Leitstellen liegt. „Dunkel" blendet nachts im Fahrzeug nicht und lässt farbige
Overlays klar hervortreten.

**Overlays**, nach Thema gruppiert:

| Gruppe | Layer |
|---|---|
| Warnungen | DWD-Warngebiete (Gemeinden), DWD-Warngebiete (Landkreise) |
| Niederschlag | DWD-Niederschlagsradar (RADOLAN), DWD-Radarvorhersage (2 h) |
| Vegetationsbrand | Waldbrandgefahrenindex, Graslandfeuerindex |
| Belastung | Gefühlte Temperatur |
| Wind | Windböen (ICON) |
| Gelände | Schummerung (Relief, Esri), Wanderwege |
| Infrastruktur | Bahnanlagen (OpenRailwayMap), Gewässer / Seezeichen |

Über dem Overlay-Bereich filtert ein Suchfeld nach Name, Gruppe und
Beschreibung — „wind", „brand" oder „warn" finden die passenden Layer, ohne
dass man ihre Namen kennt. Eine Schaltfläche schaltet alle Overlays auf einmal
ab.

**Layerprüfung gegen den Server.** Der DWD benennt Layer um, wenn sich ein
Produkt ändert, und ein falscher Name scheitert unsichtbar: Der Server
antwortet mit einer Fehlergrafik, die Leaflet als einwandfreie Kachel annimmt.
Die Schaltfläche „DWD-Layer prüfen" fragt deshalb das
GetCapabilities-Dokument ab und markiert jeden Layer als bestätigt oder
unbekannt; unbekannte werden abgeschaltet. Damit überlebt die Anwendung eine
Umbenennung ohne Codeänderung — und der Grund steht in der Oberfläche statt in
einem leeren Overlay.

**Ausbreitungskegel.** Aus Windrichtung und Ausbreitungsklasse wird ein
Gefahrenbereich in die Karte gezeichnet: ein roter Innenkreis (Vorgabe 50 m
nach FwDV 500) und ein oranger Kegel in Ausbreitungsrichtung. Der Öffnungswinkel
folgt der Stabilität — labile Schichtung streut breit, stabile Schichtung zieht
eine schmale, weit reichende Fahne. Reichweite ist frei einstellbar oder folgt
dem Richtwert der Klasse.

**Klick in die Karte** liefert Koordinaten in Grad/Dezimalminuten, Entfernung
und Peilung zum eigenen Standort — und ruft zusätzlich das **Wetter genau an
diesem Punkt** ab. Damit lässt sich ein Bereitstellungsraum oder ein
Evakuierungsziel prüfen, bevor man ihn festlegt.

Schlägt eine Kachelquelle fehl, nennt die Karte den betroffenen Layer im
Klartext, statt eine Fehlerkachel stehen zu lassen.

### Registerkarte 4 — Web-Radar

Die Regenradar-Seiten der Anbieter direkt in der Anwendung.

Jede Schnittstelle in dieser Anwendung kann ausfallen, umbenannt werden oder
still falsche Werte liefern — und jeder dieser Fälle sieht gleich aus: eine
leere Anzeige. Die eigene Seite eines Anbieters funktioniert in aller Regel
weiter, wenn dessen API es nicht tut. Damit wird aus einer Sackgasse eine
zweite Meinung, ohne die Anwendung zu verlassen.

Mitgeliefert sind:

| Ansicht | Was sie zeigt |
|---|---|
| RainViewer | weltweites Radar mit Zeitleiste und Nowcast — dieselbe Quelle wie auf der Kartenregisterkarte, hier mit voller Bedienoberfläche |
| Windy — Radar | Radaranimation zusammen mit Wind, Böen und Gewittern |
| Ventusky — Niederschlag | Niederschlagsradar mit Zeitachse; Ventusky bietet keine API an, als Seite aber uneingeschränkt nutzbar |
| Kachelmannwetter | hoch aufgelöstes Radar für Deutschland mit eigener Nachbearbeitung |
| DWD — Warnlage | amtliche Warnkarte auf Gemeindeebene |
| DWD — Niederschlagsradar | amtliches Radarbild und Radarfilm |
| NINA | Warnungen des Bundes: Gefahrstoff, Ausfälle, Bevölkerungsschutz |
| Blitzortung | Blitzeinschläge in Echtzeit — zeigt die Zugbahn einer Gewitterzelle oft früher als das Radar |
| meteoblue | Modellkarten für Niederschlag, Wind und Temperatur |
| Windy — Wind und Böen | Windfeld und Böenprognose als Strömungsbild |

Ansichten, deren Adresse die Platzhalter `{lat}`, `{lon}` und `{zoom}` trägt,
öffnen an der Einsatzstelle statt auf einer Landesübersicht und folgen einer
neuen Position. Ansichten ohne Platzhalter bleiben stehen, wo hingezoomt
wurde — eine Positionsmeldung soll den Ausschnitt nicht wegreißen.

**Eigene Adressen.** Unter „Quellen verwalten" lässt sich jede beliebige Seite
mit Namen hinterlegen, mit denselben Platzhaltern. „Aktuelle übernehmen" füllt
das Feld mit der Adresse der gewählten Ansicht, sodass eine mitgelieferte
Ansicht als Vorlage für eine angepasste dienen kann. Mitgelieferte Ansichten,
die nicht gebraucht werden, lassen sich ausblenden. Auswahl, Zoomstufe und
eigene Einträge werden sofort in der Einstellungsdatei gespeichert.

Nur `http` und `https` werden geöffnet — eine von Hand eingetragene `file:`-
oder `javascript:`-Adresse wird abgewiesen, auch wenn sie in der
Einstellungsdatei steht. Links mit `target="_blank"` bleiben in der
Registerkarte, statt ein Browserfenster ohne Bedienelemente aufzumachen, und
Downloads werden abgelehnt: Ein Speichern-Dialog auf einem Fahrzeugbildschirm
ist nur im Weg. Schlägt der Aufruf fehl, nennt die Anwendung den Grund im
Klartext — nicht die Fehlerseite des Browsers.

„Im Browser öffnen" ruft dieselbe Adresse im Standardbrowser des Rechners auf;
das funktioniert auch dort, wo die WebView2-Runtime fehlt.

### Registerkarte 5 — Verlauf

Temperatur- und Niederschlagsverlauf über die kommenden zwei Tage.

Zwei übereinanderliegende Diagramme mit gemeinsamer Zeitachse: oben Temperatur
und Taupunkt als Linien, darunter der Niederschlag als Balken. Bewusst **keine
zweite y-Achse** — zwei Skalen in einem Rahmen lassen Schnittpunkte bedeutsam
aussehen, die nur davon herrühren, wie die Skalen gewählt wurden.

Die Nachtstunden sind hinterlegt, die aktuelle Zeit ist markiert, der
Gefrierpunkt bekommt eine eigene Linie. Frost im Vorhersagezeitraum wird über
dem Diagramm im Klartext gemeldet — der eine Wert aus dieser Ansicht, der
ändert, was disponiert wird. Darunter dieselben Zahlen als Tagestabelle, damit
das Diagramm nie der einzige Weg zu ihnen ist.

Die Serienfarben stammen aus einer geprüften Palette und wurden gegen die
tatsächliche Panelfläche der Anwendung validiert (Helligkeitsband, Chroma,
Farbfehlsichtigkeits- und Normalsicht-Abstand über alle Paare, Kontrast).

### Registerkarte 6 — Diagnose

Für den Fall, dass etwas nicht funktioniert. Jede Fernabfrage der Anwendung
sieht im Fehlerfall gleich aus — eine leere Anzeige — und ohne die tatsächliche
URL und den Statuscode ist die Ursache Ratesache. Diese Registerkarte liefert
beides.

**Verbindungsprotokoll.** Jede ausgehende Anfrage mit Zeitpunkt, vollständiger
URL, Statuscode, Dauer und, im Fehlerfall, der innersten Fehlermeldung — die
nennt die eigentliche Ursache, etwa eine gescheiterte Namensauflösung oder ein
abgelehntes Zertifikat hinter einem Firmenproxy. Auch Kachelfehler der Karte
landen hier, obwohl sie gar nicht über den HTTP-Client der Anwendung laufen.

**Verbindungstest.** Prüft jeden genutzten Dienst einzeln. Fällt alles aus, liegt
es am Netz — Anmeldeportal im WLAN, Proxy oder kein Mobilfunk. Fallen einzelne
aus, ist es der Anbieter oder eine Filterung. Das unterscheidet zwei Ursachen,
die sonst gleich aussehen.

**DWD-Layerliste.** Die Liste, die der DWD-Server selbst ausgibt, mit Suchfeld
und Markierung, welche Layer eine Zeitachse haben. Das ist die verbindliche
Antwort auf die Frage, wie ein Layer wirklich heißt.

„Kopieren" legt alle drei Teile als Text in die Zwischenablage — genau das, was
für eine Fehlermeldung gebraucht wird.

### Registerkarte 7 — Einstellungen

Positionsquelle, GPS-Schnittstelle, Ortssuche, Abrufintervall,
Gefahrenbereichsradius, Protokollierung und die Quellenangaben.

#### Warnmeldungen und Alarm

Neue Warnungen melden sich von selbst: ein Signalton und ein Balken über allen
Registerkarten, der stehen bleibt, bis jemand ihn quittiert. Ein Alarm, der
nach ein paar Sekunden verschwindet, ist einer, den genau die Person verpasst,
die für diese Sekunden draußen war.

Ausgelöst wird nur bei **Änderung** — eine Warnung, die neu dazukommt, oder
eine, die sich verschärft. Nicht bei jedem Abruf: Warnungen werden alle paar
Minuten neu geholt, und ein Ton bei jedem Abruf ist der zuverlässigste Weg,
Leute darauf zu trainieren, den Ton zu ignorieren. Die Mindeststufe ist
einstellbar, Voreinstellung ist Stufe 2.

Der Ton ist bewusst die Zugabe, nicht die Hauptsache. .NET hat keine
plattformübergreifende Tonausgabe; unter Windows kommt der Systemklang, unter
macOS `afplay`, unter Linux `canberra`/`paplay`/`aplay` — und auf einer
Maschine ohne Sounddienst eben gar nichts. Genau dann steht es im Balken:
„Signalton auf diesem Rechner nicht möglich“.

#### NINA — Bevölkerungsschutz

Optional werden zusätzlich die Warnungen des Bundesamts für Bevölkerungsschutz
abgerufen: **MoWaS, KATWARN, BIWAPP** und das Hochwasserportal. Das ist eine
andere Kategorie als das Wetter und deshalb überhaupt der Grund dafür —
Gefahrstoffaustritt, Kampfmittelfund, Trinkwasserhinweis, Evakuierung,
Pegelstand. Nichts davon steht in einem Wetterfeed.

DWD-Meldungen werden aus NINA **herausgefiltert**: die kommen bereits aus zwei
eigenen Quellen, die auf die Gemeinde statt auf den ganzen Kreis auflösen.
Beide Wege zu nehmen hieße, jede Sturmwarnung zweimal und unterschiedlich
formuliert anzuzeigen — und eine doppelte Warnung ist eine, über die man
anfängt hinwegzulesen.

Das Bundessystem ist nach **Amtlichem Regionalschlüssel** geordnet, nicht nach
Koordinaten; einen Endpunkt „Warnungen in der Nähe dieses Punktes“ gibt es
nicht. Deshalb wird der Kreis einmal in den Einstellungen gewählt, so wie die
NINA-App selbst danach fragt. Über das Suchfeld genügt der Ortsname, den
zwölfstelligen Schlüssel muss niemand kennen.

Probealarme (bundesweiter Warntag) werden angezeigt, aber mit `PROBE`
gekennzeichnet und auf Stufe 1 gesetzt — sie lösen also keinen Alarm aus. Sie
zu verstecken wäre am Warntag verwirrend, sie Alarm schlagen zu lassen wäre
schlimmer.

### Wetterbericht drucken

Auf der Registerkarte „Lage & Wetter“ erzeugt **Bericht erstellen und öffnen**
eine druckfertige Seite: Kopf mit Einsatzbezeichnung, Position und Anschrift,
amtliche Warnungen im Volltext, aktuelle Messwerte, Ausbreitungsrichtung und
-klasse, ein 48-Stunden-Diagramm mit Temperatur, Taupunkt, Niederschlag und
Nachtbändern, Tagesübersicht, Sonne und Mond sowie die Einsatzhinweise.

Die Seite öffnet sich im Systembrowser — dort **Strg+P**, und als Ziel entweder
der Drucker oder „Als PDF speichern“. Das ist der Grund, warum der Bericht
HTML ist und nicht über eine PDF-Bibliothek läuft: der Druckdialog ist auf
Windows, macOS und Linux schon da, es kommt keine zu lizenzierende Abhängigkeit
ins Paket, und das Ergebnis ist überall dasselbe. Nebenbei bleibt die Datei
liegen und kann ans Einsatztagebuch geheftet oder weitergeschickt werden, ohne
sie neu zu erzeugen.

Berichte liegen unter `%APPDATA%\ELW-Meteo\Berichte`; die letzten 40 werden
aufbewahrt.

Gedruckt wird **schwarz auf weiß**, nicht im dunklen Oberflächenthema — das
wäre auf Papier unlesbar und eine Tonerpatrone teuer. Temperatur- und
Taupunktkurve unterscheiden sich zusätzlich durch die Strichelung, damit sie
auch aus einem Schwarzweißdrucker auseinanderzuhalten sind. Ist der Bericht aus
dem gespeicherten Stand erzeugt, steht das als Balken darauf: ein Ausdruck
überlebt die Sitzung, die ihn erzeugt hat, und einer ohne Altersangabe ist eine
Falle.

#### Programm-Aktualisierung

Die Anwendung sieht einmal täglich beim GitHub-Repository nach, ob es eine
neuere Freigabe gibt, und wählt das Paket für die laufende Plattform aus.
Heruntergeladen und eingespielt wird **nichts** ohne ausdrücklichen Klick —
drei Schritte, drei Klicks:

1. **Prüfen** — fragt die Freigaben ab und zeigt Version, Paketgröße und die
   Freigabenotizen. Passiert bei aktivierter Einstellung auch von selbst,
   höchstens einmal je Intervall (Voreinstellung 24 Stunden; GitHub erlaubt
   ohne Anmeldung 60 Anfragen je Stunde).
2. **Herunterladen und prüfen** — lädt das Paket, vergleicht Größe und
   SHA-256 mit dem, was die GitHub-API gemeldet hat, entpackt es in einen
   Zwischenordner und prüft, dass das Programm darin überhaupt enthalten ist.
   Die Installation wird dabei nicht angefasst.
3. **Einspielen und neu starten** — der einzige Schritt, der die Installation
   verändert.

**Warum drei Schritte und nicht einer.** Eine Anwendung, die sich selbst
herunterlädt und neu startet, tut das irgendwann in dem Moment, in dem jemand
eine Windrichtung von ihr abliest. Das ist schlimmer als eine veraltete
Version.

**Wie der Austausch abläuft.** Ein laufendes Programm kann seine eigenen
Dateien nicht ersetzen — unter Windows ist die `.exe` gesperrt. Deshalb
schreibt die Anwendung ein kleines Skript und beendet sich. Das Skript
wartet auf das Ende des Prozesses (höchstens 60 Sekunden, danach bricht es
ab — es beendet den Prozess **nicht** von sich aus), benennt die alte
Installation zur Seite, verschiebt die neue an ihre Stelle und startet sie.
Scheitert das Verschieben, wird die alte Installation zurückgeholt.

Gelöscht wird nichts: die vorige Version bleibt als Nachbarordner
`…​.vor-<Version>` liegen, und alles Weitere protokolliert
`%APPDATA%/ELW-Meteo/Update/apply-update.log`. Ein halb ersetzter Ordner ist
der eine Ausgang, der wirklich weh tut — vermischte Programmteile aus zwei
Versionen scheitern beim Laden und sehen aus wie ein kaputter Rechner, nicht
wie eine fehlgeschlagene Aktualisierung.

**Grenzen, offen gesagt.** Liegt die Anwendung in einem schreibgeschützten
Ordner — typisch unter `C:\Programme` — verweigert sie den Austausch und sagt
das, statt es zu versuchen. Die Prüfsumme schützt gegen einen abgebrochenen
Download über eine Mobilfunkstrecke, sie ist **keine Signatur**: Datei und
Prüfsumme kommen von derselben Stelle. Der eigentliche Vertrauensanker ist,
dass beides über TLS von `api.github.com` geholt wird.

---

## Positionsbestimmung

Drei Betriebsarten:

1. **Automatisch** — GPS, sonst IP-Ortung, sonst der hinterlegte Standort
2. **Nur GPS-Empfänger**
3. **Manuelle Koordinaten**

**GPS über serielle Schnittstelle.** Viele Einsatzleitwagen haben bereits einen
NMEA-0183-Empfänger an einer (virtuellen) COM-Schnittstelle. Die Anwendung
liest `GGA`, `RMC` und `GLL` von beliebigen Talker-IDs (GP/GN/GL/GA), prüft die
Prüfsumme, verwirft Sätze ohne gültigen Fix und schätzt die Genauigkeit aus dem
HDOP. Das ist metergenau und funktioniert ohne Mobilfunk.

**IP-Ortung** ist der Notnagel: hinter einem Mobilfunkrouter kann sie zig
Kilometer danebenliegen. Die Oberfläche kennzeichnet sie deshalb ausdrücklich
als ungenau.

**Ortssuche** in den Einstellungen setzt die manuelle Position per Namenssuche;
die aktuelle Position wird zusätzlich in eine Adresszeile rückaufgelöst.

---

## Installation unter Windows

Es gibt drei Wege, und der erste ist für die meisten der richtige.

### MSI-Paket

`ELW-Meteo-<version>-win-x64.msi` von der Freigabeseite laden und doppelklicken.
Installiert **ohne Administratorrechte** für den angemeldeten Benutzer nach
`%LocalAppData%\Programs\ELW-Meteo`, legt Start- und Desktop-Verknüpfung an und
erscheint in „Programme und Features“. Die Runtime ist enthalten.

Für eine stille Installation, etwa beim Einrichten mehrerer Fahrzeugrechner:

```powershell
msiexec /i ELW-Meteo-1.4.0-win-x64.msi /qn
```

| Schalter | Wirkung |
|---|---|
| `INSTALLDESKTOPSHORTCUT=0` | keine Desktop-Verknüpfung |
| `/qn` | ohne Oberfläche |
| `/l*v protokoll.log` | ausführliches Protokoll |

**Was der Installer nicht anfasst:** `%APPDATA%\ELW-Meteo` mit Einstellungen,
CSV-Protokoll und Berichten bleibt beim Deinstallieren stehen. Die eingerichtete
Fahrzeugkonfiguration ist der Teil, der jemanden einen Nachmittag gekostet hat;
ein Update-Zyklus, der sie stillschweigend wegwirft, ist einer, den niemand ein
zweites Mal fährt.

**Warum ausschließlich pro Benutzer.** Ein Paket, das beides kann, sieht besser
aus, bis man es baut. Der einzige Doppelmodus, den ausgeliefertes WiX anbietet,
schreibt zwingend `MSIINSTALLPERUSER=1` ins Paket. Diese Eigenschaft zieht die
Verzeichnisauflösung ins Benutzerprofil, während der Installationsbereich
getrennt davon aus `ALLUSERS` und den Rechten des Aufrufers entschieden wird —
und bei einer erhöhten stillen Installation gehen die beiden auseinander:
Dateien im Profil eines Benutzers, Deinstallationseintrag für die ganze
Maschine. Damit bietet die Maschine jedem Konto die Deinstallation fremder
Dateien an.

Fest auf pro Benutzer gibt es diese Zweideutigkeit nicht. Nebenbei bleibt so
die [Selbst-Aktualisierung](#programm-aktualisierung) arbeitsfähig — unter
`C:\Programme` dürfte die laufende Anwendung nicht schreiben und müsste den
Austausch verweigern.

Wer eine geräteweite Installation braucht, nimmt das ZIP-Archiv: das ist
ehrlicher als ein Installer, der etwas anderes tut, als sein Name sagt.

**WebView2** wird bei der Installation geprüft, aber nicht mitinstalliert. Fehlt
die Runtime, sagt das der letzte Bildschirm der Installation, und im
Programmordner liegt eine `HINWEIS-WebView2.txt`. Ungefragt eine
Microsoft-Komponente auf fremde Fahrzeugrechner zu schieben wäre die falsche
Entscheidung — betroffen ist ohnehin nur die Kartenregisterkarte.

### ZIP-Archiv

`…-win-x64-standalone.zip` entpacken, `ELW-Meteo.exe` starten. Keine
Installation, kein Eintrag, nichts in der Registry — der Weg für einen
USB-Stick oder einen Rechner, auf dem nichts installiert werden darf.

### Aus dem Quelltext

Siehe unten.

---

## Bauen und starten

Voraussetzungen:

- Windows 10 oder 11
- .NET SDK 8.0 oder neuer
- Microsoft Edge **WebView2-Runtime** — auf aktuellen Windows-Installationen
  bereits vorhanden. Fehlt sie, bleibt nur die Kartenregisterkarte leer und
  zeigt einen Hinweis; alles andere funktioniert weiter.

```powershell
git clone https://github.com/FlorianGross/ELW-Meteo.git
cd ELW-Meteo

dotnet build -c Release
dotnet run --project src/ElwMeteo.App
```

Eigenständiges Verzeichnis zum Verteilen:

```powershell
dotnet publish src/ElwMeteo.App -c Release -r win-x64 --self-contained false -o publish
```

Tests (laufen auf jeder Plattform, da die Fachlogik plattformneutral ist):

```powershell
dotnet test
```

MSI-Paket bauen (nur unter Windows — WiX setzt es voraus):

```powershell
dotnet tool install --global wix --version 5.0.2
dotnet publish src/ElwMeteo.App -c Release -r win-x64 --self-contained true -o publish
pwsh installer/build-installer.ps1 -PublishDirectory publish -Version 1.4.0 -OutputPath ELW-Meteo.msi
```

Die Dateiliste des Pakets wird dabei aus dem Veröffentlichungsordner erzeugt
(`installer/Files.generated.wxs`, nicht eingecheckt). Eine von Hand gepflegte
Liste veraltet beim ersten neuen Paket, und zwar lautlos: das Paket installiert
sich, und dann startet die Anwendung nicht, weil eine Assembly nie aufgeführt
war.

`installer/test-installer.ps1` installiert und deinstalliert das erzeugte Paket
in beiden Modi und prüft Ablageort, Verknüpfungsziel, Eintrag in „Programme und
Features“ und dass die Einstellungen überleben. Die CI führt das bei jedem Pull
Request aus — ein Installer ist das eine Erzeugnis, dessen Fehler sonst erst auf
einem fremden Rechner auffallen.

---

## CI/CD

Drei GitHub-Actions-Abläufe:

| Ablauf | Auslöser | Was er tut |
|---|---|---|
| `build.yml` | jeder Push und Pull Request | Baut und testet auf `windows-latest`, veröffentlicht das Ergebnis als Artefakt (30 Tage). Ein zweiter Job baut die Fachlogik auf `ubuntu-latest` — schlägt er fehl, ist eine WPF-Abhängigkeit nach `ElwMeteo.Core` gelangt. Ein dritter Job prüft die Codeformatierung, aber nur beratend (`continue-on-error`), damit eine Stilfrage nie eine Korrektur aufhält. |
| `release.yml` | Tag `v*` oder manuell | Testet, baut zwei Pakete — eines für Rechner mit installierter .NET-8-Desktop-Runtime, eines standalone mit mitgelieferter Runtime — und legt ein GitHub-Release mit beiden ZIPs und automatischen Release Notes an. |
| `dependabot.yml` | monatlich | Aktualisiert NuGet-Pakete und Actions; Testwerkzeuge werden zu einem Pull Request gebündelt. |

Release schneiden:

```powershell
git tag -a v1.1.0 -m "ELW-Meteo 1.1.0"
git push origin v1.1.0
```

NuGet-Pakete werden zwischen Läufen gecacht, die Testergebnisse als `.trx`
hochgeladen.

---

## Projektaufbau

```
src/ElwMeteo.Core/     net8.0      — Fachlogik, plattformneutral und testbar
  Time/                             taktische Zeit, Sonnenstand, Mondphase
  Meteorology/                      Thermodynamik, Beaufort, Ausbreitungsklasse,
                                    Geodäsie, Ausbreitungskegel, WMO-Codes
  Models/                           Position, Wetterdaten, DWD-Warnung
  Assessment/                       Ableitung der Einsatzhinweise
  Services/                         Open-Meteo, DWD, NINA, RainViewer, NMEA,
                                    Geocoding, Warnungsmelder
  Reporting/                        Textblöcke, CSV-Protokoll, Druckbericht
                                    mit SVG-Diagramm
  Persistence/                      letzter Stand für den Start ohne Netz
  Configuration/                    Einstellungen
  Maps/                             Layer-Katalog, Radar- und Webquellen
  Updates/                          Versionsvergleich, GitHub-Freigaben,
                                    Paketauswahl, Prüfsumme, Austauschskript

src/ElwMeteo.Presentation/ net8.0  — Ansichtsmodelle, von beiden Oberflächen
  ViewModels/                       je Registerkarte plus Uhr und Schale
  Services/                         GPS-Schnittstelle, Positionsauflösung,
                                    Berichtsausgabe
  Charting/                         Diagrammmodell ohne Zeichentypen
  Platform/                         IUiDispatcher, IUiTimer, IClipboardService,
                                    IShellLauncher, IAlertSignal, UiColour

src/ElwMeteo.Desktop/  net8.0      — Avalonia-Oberfläche (Linux, macOS, Windows)
  Views/                            dieselben sieben Registerkarten
  Controls/TrendChart.cs            Diagramm, für Avalonia gezeichnet
  Platform/                         Avalonia-Antworten auf die Schnittstellen

src/ElwMeteo.App/      net8.0-windows — WPF-Oberfläche (Windows)
  Views/                            Uhr, Dashboard, Karte, Web-Radar, Verlauf,
                                    Diagnose, Einstellungen
  Controls/                         Diagramm für den Wetterverlauf
  Assets/map.html                   Leaflet-Karte für WebView2
  Assets/elw-meteo.ico              Anwendungssymbol (aus tools/make-icon.py)
  Themes/                           dunkles, kontrastreiches Farbschema

installer/ELW-Meteo.wxs             MSI-Paket: Verzeichnisse, Verknüpfungen,
                                    WebView2-Prüfung, Doppelmodus
installer/build-installer.ps1       erzeugt die Dateiliste und baut das MSI
installer/test-installer.ps1        installiert und deinstalliert es wirklich

tools/make-icon.py                  erzeugt das Anwendungssymbol reproduzierbar

tests/ElwMeteo.Core.Tests/          xUnit — 542 Tests
tests/ElwMeteo.Desktop.Tests/       Avalonia-Rauchtests, kopflos — 10 Tests
```

Die gesamte Fachlogik liegt in `ElwMeteo.Core`, alle Ansichtsmodelle in
`ElwMeteo.Presentation` — beide ohne jede Abhängigkeit zu WPF oder Avalonia.
Die wenigen Dinge, die eine Oberfläche entscheiden muss — auf welchem Faden
gearbeitet wird, was eine Zwischenablage ist, wie ein Zeitgeber tickt, was aus
einer Farbe wird — liegen als Schnittstellen in `Presentation/Platform` und
werden von jeder Ausgabe einmal beantwortet.

Das ist keine Formsache: die CI baut und testet diesen Teil auf Ubuntu, macOS
und Windows, und die Avalonia-Ansichten werden dabei kopflos wirklich
aufgebaut. Ein Windows-spezifischer Typ im Kern lässt den Lauf sofort rot
werden.

 Sonnenstandsberechnung, Ausbreitungsklassen, NMEA-Dekodierung und alle
Parser sind damit ohne Oberfläche testbar — die Sonnenzeiten sind gegen
veröffentlichte Almanachwerte für Frankfurt am Main geprüft.

---

## Datenquellen

| Was | Quelle | Bedingungen |
|---|---|---|
| Messwerte, Nowcast, Vorhersage | [Open-Meteo](https://open-meteo.com) (ICON des DWD) | CC BY 4.0, kein Schlüssel nötig |
| Amtliche Warnungen, Fachkarten | [DWD GeoServer](https://maps.dwd.de) | Open Data nach GeoNutzV |
| Bevölkerungsschutz (MoWaS, KATWARN, BIWAPP, Hochwasser) | [NINA / warnung.bund.de](https://warnung.bund.de) | öffentliche Schnittstelle des BBK, kein Schlüssel; optional, standardmäßig aus |
| Radarbilder, Nowcast und Infrarot-Satellit | [RainViewer](https://www.rainviewer.com/) | kostenfreie öffentliche API |
| Stationsmesswerte, DWD-Warnungen (CAP), RADOLAN am Punkt | [Bright Sky](https://brightsky.dev) | freier JSON-Zugang zu DWD Open Data, ohne Schlüssel |
| Luftbild und Reliefschummerung | Esri / ArcGIS Online | kostenfrei mit Quellenangabe |
| Radarkacheln (optional) | OpenWeatherMap | benötigt einen eigenen kostenlosen Schlüssel |
| Kartengrundlage | OpenStreetMap, OpenTopoMap | ODbL bzw. CC BY-SA |
| Adressauflösung | Nominatim | Nutzungsrichtlinie, identifizierender User-Agent gesetzt |
| Eingebettete Radarseiten | RainViewer, Windy, Ventusky, Kachelmannwetter, DWD, NINA, Blitzortung, meteoblue | öffentliche Seiten, als gewöhnlicher Seitenaufruf geöffnet — Marke, Quellenangabe und Nutzungsbedingungen des Anbieters bleiben sichtbar |

Keine Registrierung, keine API-Schlüssel.

Die Layernamen des DWD-GeoServers ändern sich gelegentlich mit einem
Produktwechsel. Bleibt ein Overlay leer, hilft ein Abgleich mit
`https://maps.dwd.de/geoserver/dwd/wms?service=WMS&request=GetCapabilities`
und eine Anpassung in `src/ElwMeteo.Core/Maps/MapLayerCatalog.cs` — die Layer
liegen dort als Daten, nicht im Kartencode.

### Offline-Betrieb

Ohne Netz laufen Uhr, taktische Zeit, Sonnenstand und Mondphase
unverändert weiter; die Wetterdaten bleiben mit sichtbarer Altersangabe stehen
und werden nach 20 Minuten als veraltet markiert. Karte und Radar brauchen
zwingend eine Verbindung.

**Der letzte Stand übersteht den Neustart.** Nach jedem vollständigen Abruf
werden Messwerte, Warnungen und Anschrift nach
`%APPDATA%\ELW-Meteo\last-state.json` geschrieben. Startet die Anwendung ohne
erreichbares Netz, erscheint dieser Stand sofort — mit einem bernsteinfarbenen
Balken darüber, der Uhrzeit und Alter nennt. Der gespeicherte Stand
überschreibt **nie** einen frisch abgerufenen; er erscheint nur, wenn in dieser
Sitzung noch nie etwas angekommen ist.

Nach **zwölf Stunden** wird er verworfen statt angezeigt. Eine Windrichtung von
gestern Abend ist nicht alt, sondern falsch, und sie anzubieten wäre schlimmer
als die leere Anzeige, die das Ganze ersetzen soll.

Geschrieben wird in eine Nebendatei und dann umbenannt: ein Stromausfall
mitten im Schreiben lässt die vorige Datei heil, statt eine abgeschnittene zu
hinterlassen.

### Fehlgeschlagener Abruf

Ein misslungener Abruf wird bis zu dreimal wiederholt — nach 20, 40 und 60
Sekunden, mit sichtbarer Restzeit. Auf einem Fahrzeug ist das Funkloch der
Normalfall, nicht die Ausnahme; ohne Wiederholung stünde die Fehlermeldung bis
zum nächsten regulären Intervall, bei 30 Minuten Einstellung also eine halbe
Stunde.

Leaflet wird in `Assets/map.html` von einem CDN geladen. Soll die Kartenseite
selbst ohne Internet starten (etwa mit einem lokalen Kachel-Cache), genügt es,
`leaflet.js` und `leaflet.css` neben die Datei zu legen und die beiden
`<link>`/`<script>`-Verweise auf relative Pfade umzustellen.

---

## Wichtiger Hinweis

Diese Anwendung ist eine **Entscheidungshilfe**. Sie ersetzt weder die
amtliche Warnlage des Deutschen Wetterdienstes noch die Beurteilung durch die
Einsatzleitung.

Der Ausbreitungskegel ist eine Orientierungshilfe für die Erstphase im Sinne
der Faustwerte der FwDV 500 — **kein Ausbreitungsmodell**. Er berücksichtigt
weder Gelände noch Bebauung, Quellstärke oder Stoffeigenschaften. Für alles
über die ersten Minuten hinaus gehört eine richtige Ausbreitungsrechnung
herangezogen.

Der Ångström-Index ist ein Screening-Wert aus Temperatur und Luftfeuchte; die
Referenz für die Waldbrandgefahr bleibt der WBI des DWD, der als Kartenlayer
eingeblendet werden kann.
