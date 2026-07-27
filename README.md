# ELW-Meteo

Desktop-Anwendung für den Einsatzleitwagen: Uhrzeit, taktische Zeit und die
meteorologische Lage an der eigenen Position — plus eine Kartenansicht mit
Regenradar, Nowcast und DWD-Fachkarten.

Windows-Desktop, C# / .NET 8, WPF.

---

## Was die Anwendung zeigt

### Registerkarte 1 — Lage & Wetter

**Kopfzeile (immer sichtbar)**

| Anzeige | Format | Zweck |
|---|---|---|
| Ortszeit | `13:47:12`, Wochentag, MEZ/MESZ | Wanduhr |
| Taktische Zeit | `271347BJUL26` | Date-Time-Group nach NATO/BOS |
| Taktische Zeit Zulu | `271147ZJUL26` | dieselbe Zeit in UTC |
| UTC | `11:47:12` | für überregionale Abstimmung |
| Einsatzdauer | `2:13:05` | Stoppuhr ab Einsatzbeginn, mit Start/Stopp und Beginnzeitstempel |

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

**Amtliche DWD-Warnungen** für die Gemeinde an der aktuellen Position, mit
Warnstufe, Gültigkeit, Beschreibung und Verhaltenshinweis.

**Nowcast-Streifen** — Niederschlag der nächsten drei Stunden in
15-Minuten-Schritten als Balken, mit Klartextzusammenfassung („Niederschlag
beginnt in ca. 30 min“).

**Dokumentation** — zwei Schaltflächen kopieren fertige Textblöcke:
eine vollständige Wettermeldung für das Einsatztagebuch und eine Kurzfassung
zum Absetzen über Funk. Zusätzlich wird jeder Abruf als CSV-Zeile
protokolliert (Semikolon-getrennt, deutsche Dezimalkommas — öffnet in Excel
ohne Importassistent).

### Registerkarte 2 — Karten & Radar

Leaflet-Karte in einem WebView2-Steuerelement.

**Regenradar mit Zeitleiste.** Rund zwei Stunden gemessene Radarkomposite plus
30 Minuten Nowcast in 10-Minuten-Schritten. Abspielen, Einzelschritt, Sprung
auf „jetzt“, einstellbare Deckkraft. Beim Bildwechsel wird das neue Bild erst
eingeblendet, wenn seine Kacheln geladen sind — kein Flackern.

**Grundkarten:** OpenStreetMap, OpenStreetMap.de, OpenTopoMap (Höhenlinien),
CyclOSM (betont Wirtschaftswege und Pfade).

**Overlays**, nach Thema gruppiert:

| Gruppe | Layer |
|---|---|
| Warnungen | DWD-Warngebiete (Gemeinden), DWD-Warngebiete (Landkreise) |
| Niederschlag | DWD-Niederschlagsradar (RADOLAN), DWD-Radarvorhersage (2 h) |
| Vegetationsbrand | Waldbrandgefahrenindex, Graslandfeuerindex |
| Belastung | Gefühlte Temperatur |
| Wind | Windböen (ICON) |
| Gelände | Schummerung (Relief), Gewässer / Seezeichen |

**Ausbreitungskegel.** Aus Windrichtung und Ausbreitungsklasse wird ein
Gefahrenbereich in die Karte gezeichnet: ein roter Innenkreis (Vorgabe 50 m
nach FwDV 500) und ein oranger Kegel in Ausbreitungsrichtung. Der Öffnungswinkel
folgt der Stabilität — labile Schichtung streut breit, stabile Schichtung zieht
eine schmale, weit reichende Fahne. Reichweite ist frei einstellbar oder folgt
dem Richtwert der Klasse.

**Klick in die Karte** liefert Koordinaten in Grad/Dezimalminuten sowie
Entfernung und Peilung zum eigenen Standort.

### Registerkarte 3 — Einstellungen

Positionsquelle, GPS-Schnittstelle, Ortssuche, Aktualisierungsintervall,
Gefahrenbereichsradius, Protokollierung und die Quellenangaben.

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

---

## Projektaufbau

```
src/ElwMeteo.Core/     net8.0      — Fachlogik, plattformneutral und testbar
  Time/                             taktische Zeit, Sonnenstand, Mondphase
  Meteorology/                      Thermodynamik, Beaufort, Ausbreitungsklasse,
                                    Geodäsie, Ausbreitungskegel, WMO-Codes
  Models/                           Position, Wetterdaten, DWD-Warnung
  Assessment/                       Ableitung der Einsatzhinweise
  Services/                         Open-Meteo, DWD, RainViewer, NMEA, Geocoding
  Reporting/                        Textblöcke und CSV-Protokoll
  Configuration/                    Einstellungen
  Maps/                             Layer-Katalog

src/ElwMeteo.App/      net8.0-windows — WPF-Oberfläche (MVVM)
  Views/                            Dashboard, Karte, Einstellungen
  ViewModels/                       je Registerkarte plus Uhr und Shell
  Services/                         GPS-Schnittstelle, Positionsauflösung
  Assets/map.html                   Leaflet-Karte für WebView2
  Themes/                           dunkles, kontrastreiches Farbschema

tests/ElwMeteo.Core.Tests/          xUnit — 164 Tests
```

Die gesamte Fachlogik liegt in `ElwMeteo.Core` und hat keine Abhängigkeit zu
WPF. Sonnenstandsberechnung, Ausbreitungsklassen, NMEA-Dekodierung und alle
Parser sind damit ohne Oberfläche testbar — die Sonnenzeiten sind gegen
veröffentlichte Almanachwerte für Frankfurt am Main geprüft.

---

## Datenquellen

| Was | Quelle | Bedingungen |
|---|---|---|
| Messwerte, Nowcast, Vorhersage | [Open-Meteo](https://open-meteo.com) (ICON des DWD) | CC BY 4.0, kein Schlüssel nötig |
| Amtliche Warnungen, Fachkarten | [DWD GeoServer](https://maps.dwd.de) | Open Data nach GeoNutzV |
| Radarbilder und Nowcast-Kacheln | [RainViewer](https://www.rainviewer.com/) | kostenfreie öffentliche API |
| Kartengrundlage | OpenStreetMap, OpenTopoMap | ODbL bzw. CC BY-SA |
| Adressauflösung | Nominatim | Nutzungsrichtlinie, identifizierender User-Agent gesetzt |

Keine Registrierung, keine API-Schlüssel.

Die Layernamen des DWD-GeoServers ändern sich gelegentlich mit einem
Produktwechsel. Bleibt ein Overlay leer, hilft ein Abgleich mit
`https://maps.dwd.de/geoserver/dwd/wms?service=WMS&request=GetCapabilities`
und eine Anpassung in `src/ElwMeteo.Core/Maps/MapLayerCatalog.cs` — die Layer
liegen dort als Daten, nicht im Kartencode.

### Offline-Betrieb

Ohne Netz laufen Uhr, taktische Zeit, Einsatzdauer, Sonnenstand und Mondphase
unverändert weiter; die Wetterdaten bleiben mit sichtbarer Altersangabe stehen
und werden nach 20 Minuten als veraltet markiert. Karte und Radar brauchen
zwingend eine Verbindung.

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
