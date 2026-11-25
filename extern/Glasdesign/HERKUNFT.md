# Glasdesign - feste Kopie in JG TacRelay

Eigene WPF-Designbibliothek des Maintainers (Glaskörper, Glasknöpfe, Reiter, Hilfe-System, Farbstellungen). Sie wird in
einem eigenen Projekt gepflegt; hier liegt eine feste Kopie, die JG TacRelay einbindet.

| Angabe | Wert |
| --- | --- |
| Quelle | `C:\Claude\work\WPF Net 10 Glasdesign\src\Glasdesign` (eigenes Projekt, eigener Chat) |
| Fassung | 0.5.0-beta.13 mit den Farbstellungen Anthrazit und Weinrot (laut `Directory.Build.props` des Glasdesign-Projekts 0.5.0-beta.13; beide kamen ohne neue Fassung dazu) |
| Stand | Commit 0fd06639e65fb8c942f5808b16ceb4ea161e1563 ("Farbstellung Weinrot statt des alten Namens, auch neben Anthrazit") auf dem Zweig `farbstellung/anthrazit`, abgezweigt von `entwicklung` bei 3c2af68 (Tag `v0.5.0-beta.13`), davor 5c0cee0 (Farbstellung Anthrazit); noch nicht nach `entwicklung` übernommen, nicht gepusht. Die Dateien sind die Blobs dieses Commits, mit `git cat-file blob` geschrieben, nicht aus dem Arbeitsordner kopiert; jede außer `Thema.cs` ist Byte für Byte gleich ihrem Blob. |
| Nicht kopiert | `Farbstellungen/Weinrot.xaml`: JG TacRelay nutzt nur Anthrazit (Sarevoc, 18./19.10.2026, Q5, Q24). `Thema` findet die Farbstellungen über die eingebetteten Dateien; ohne sie gibt es hier nur Standard und Anthrazit. |
| Bewusste Abweichung | `Thema.cs`: Zeile 34 des Blobs `3f3663e953c43d669289ea106305e768775cc863` fehlt, der einzige Eintrag der Tabelle `FruehereNamen`. Er bildet den früheren Namen der Farbstellung Weinrot auf "Weinrot" ab, damit gespeicherte Werte in Glint und anderen Programmen weiter laden. JG TacRelay braucht ihn nicht: Es ruft `Thema.Laden`, `Thema.Setzen` und `Thema.Quelltext` nie mit einem Namen auf (App.xaml bindet `Farbstellungen/Anthrazit.xaml` unmittelbar ein), und Weinrot ist hier nicht eingebaut, der Eintrag führte also ohnehin ins Leere. Ohne ihn steht der alte Name nirgends mehr im Repository (Namenswächter ohne Ausnahme). Die Tabelle bleibt leer stehen; `HeutigerName` gibt dann jeden Namen unverändert zurück. Der Kommentar über der Tabelle bleibt wie im Blob. Blob: 11540 Bytes, SHA-256 `9ec97f330d97d5c20741be998fa1acc10be8b977e058602e1bbcfe63714d6736`; die Kopie (11506 Bytes) steht in der Tabelle unten. Bei der nächsten Kopie dieselbe Zeile wieder herausnehmen, solange das Glasdesign-Projekt sie trägt. Dazu seit 0.13.1 (25.11.2026, öffentliche Fassung): In den Kommentaren von 22 Dateien steht statt des Rufnamens des Maintainers "Sarevoc"; Längen und SHA-256 in der Tabelle sind die nach dieser Änderung, der Code ist unverändert. |
| Prüfung vor dem Kopieren | Tests des Glasdesign-Projekts am Commit 0fd0663 (per `git archive` nach `C:\Claude\tmp\umbenennen\gd-0fd0663` ausgepackt und dort gebaut): 306 von 306 bestanden (Abschnitt Prüfung); Ergebnis in `C:\Claude\tmp\umbenennen\gd-0fd0663-mess\testlauf-ergebnis.txt` |
| Kopiert | 19.10.2026, nur `src\Glasdesign` ohne `bin` und `obj` und ohne `Farbstellungen/Weinrot.xaml`; ersetzt die Kopie vom Commit 5c0cee0 (Umbau-Schritt 2). Gegenüber ihr geändert: `Aufzaehlungen.cs`, `Glas.Stile.xaml`, `Glas.xaml`, `Hilfe.cs`, `Pultexport.cs`, `Schluessel.cs` (nur Kommentare, neuer Name der Farbstellung) und `Thema.cs` (Tabelle früherer Namen und `HeutigerName`, Eintrag wie oben herausgenommen). |
| Lizenz | wie JG TacRelay: Maintainer Sarevoc, alle Rechte bei Sarevoc, Lizenz noch nicht entschieden (eigene Runde); eigener Code, nichts Fremdes darin |

Was sich gegenüber 0.3.1 ändert (bis Umbau-Schritt 2 lag hier 0.3.1, Commit de8a98d; laut Glasdesign-Projekt, LIESMICH.md
Abschnitt 0): Glas 4 (Glanzprofil, Bauplan und Stilmaße je Farbstellung, Leuchtsaum statt des weißen Fokusrahmens,
Spotlicht, Lichtkante), neue Stilfamilien und Steuerelemente, Fortschritt, Fensterrahmen und die Elemente der Stufe 2.
Alles Neue sind eigene `Glas.*`-Stile; Vorgabestile für bestehende Programme kamen nicht dazu. Laut Fassungsvergleich zu
0.4.0-rc.2 zeichnen die alten Stile gegen 0.3.1 in Ruhe, gedrückt und gesperrt pixelgleich außer dem gewählten Reiter, kein
Schlüssel ist entfallen. JG TacRelay nimmt seit 0.11.2 keine Glasknöpfe mehr, sondern nur noch Farbstellung, Hilfe-System
und Tooltips. Die Farbstellung **Anthrazit** (Grau, neutral und ruhig, Akzent Petrol #2F8A8C, hell #4FB3B4) bindet App.xaml
ein. Mit 0fd0663 heißt die dritte Farbstellung des Glasdesign-Projekts Weinrot statt mit ihrem alten Namen; Farben und
Schlüssel blieben gleich.

Zeilenenden: Die Dateien haben LF wie im Glasdesign-Repository. `extern/** -text` in `.gitattributes` hält sie beim Einchecken
und Auschecken Byte für Byte gleich, damit die Prüfsummen unten immer stimmen.

Gebaut wird die Kopie mit den Analyse- und Warnungseinstellungen von JG TacRelay (TreatWarningsAsErrors, AnalysisLevel
latest-recommended, Nullable). `extern\Directory.Build.props` gibt ihr nur ihren eigenen Produktnamen und ihre Fassung
0.5.0-beta.13 statt der von JG TacRelay.

Nicht benutzt in JG TacRelay: der Leuchtschalter mit allem, was dazugehört (`Leuchtschalter.cs`, `Leuchtgeometrie.cs`,
`Leuchtzeichen.cs`, Stile `Glas.Leuchtschalter*`, Schlüssel `Glas.Leucht.*`), das Einstellpult (`Pultexport.cs`) und die
Stilfamilien. Sarevoc, 02.10.2026: "die JG-Schalter sind nicht für JG TacRelay, kein JG im Knopf". Die Dateien bleiben in der
Kopie, damit sie bis auf die eine Zeile oben unverändert bleibt; der Test
`OberflaecheGlasTests.Glas_KeinLeuchtschalterUndKeinJgZeichenInTacRelay` hält fest, dass Funk.* nichts vom Leuchtschalter
benutzt.

Ändern: nie hier. Neue Fassung im Glasdesign-Projekt bauen und prüfen, dann neu kopieren (die Abweichung in `Thema.cs`
wieder herstellen) und diese Datei neu schreiben.

## Prüfung

Tests des Glasdesign-Projekts am Commit 0fd0663, gebaut aus einer `git archive`-Kopie unter
`C:\Claude\tmp\umbenennen\gd-0fd0663` (19.10.2026): 306 bestanden, 0 fehlgeschlagen, 306 gesamt (492,7 s). Darin alle
Prüfungen der Farbstellungen über Standard, Weinrot und Anthrazit (Kontrast, Befunde, Daten, Eingabe, Navigation,
Rückmeldungen, Familien, Export, Thema) und der Test, dass der frühere Name als Weinrot lädt; die Zeitmessungen, die beim
Lauf für 5c0cee0 unter fremder Last rot waren, bestanden diesmal. Die gebaute Kopie hier mit der fehlenden Zeile: ohne
Warnung und Fehler gebaut, und in den Tests von JG TacRelay prüft `Glas_KopieGleichHerkunft` Dateien, Längen und
SHA-256 unten sowie, dass nur Standard und Anthrazit eingebaut sind.

## SHA-256 je Datei

| Datei | Bytes | SHA-256 |
| --- | ---: | --- |
| `Abstandsfeld.cs` | 15141 | `45b58e169293e039cce92c5f6a856a3d641408e6aa87b5a18e16a3f7fd67a741` |
| `Abzeichen.cs` | 10007 | `4b7922c087ad5c925c0c11f32e97cd13ea2449fde4f2ec7c51a61caaaa4740a5` |
| `Animationen.cs` | 3124 | `86106220727e3e53f1c812b832bb0fd102f7a471966877c98fa1dfddf69f964b` |
| `Aufkleber.cs` | 24152 | `d3a8ea973f24a3d65264b16fc501a4bb7f8542ab70d7926890c83ff4a3452e96` |
| `Aufzaehlungen.cs` | 9388 | `30162d13fd52f086f0fcc6865cbae4841305063324cd6820b753103a23d1147e` |
| `Avatar.cs` | 22979 | `0cc3c9d1cbcd818c3a723066e9ade713bb443b04aba592b501b3eb22cc0dbabf` |
| `Bauplan.cs` | 5171 | `177a97647da88c18f9c2fc8c314bd6eed3c131663901d78bf53bffc0042d933d` |
| `Bildraster.cs` | 17727 | `afaa7858205f7dd749211789f7906e85f72caf03e5f41d71a0a9da4a20db7ca1` |
| `Brotkrumen.cs` | 13441 | `b728a73784047aa5f72ef677f493abe41163f26830a1fc49eb211caf792988fc` |
| `Chromrahmen.cs` | 16926 | `1c2cd8690f6ddf71600a286b68fe0b19bb649c916546de34abee614a1f9c3aaf` |
| `Chromrand.cs` | 11171 | `75ac70a8635735fbd0bb51962f80d54e70cdb411d18713dae8642df4ef8d211a` |
| `Dialog.cs` | 16289 | `118b41009d9d48660df96d08eb9c968cab93bf67b6b619183092a6cfdc96712b` |
| `Dickglas.cs` | 30446 | `eacd74cd15c180d6e93dbf7ae8c062054fafe858547dbe60daa8a2855d680899` |
| `Eingabe.cs` | 28897 | `b0b9da1a5e30d30211cbe0c115f1541e946ced0ad53db04308b181b3c608db0d` |
| `Familienkoerper.cs` | 34670 | `07a944f1423ca32af88c646cd3a7873f8011dd2084c59431654dd4dcdcf8d7ea` |
| `Familienteile.cs` | 23294 | `19f116a2d2a925301622d59441c19283939f4e107b8fb94d6218a9bc60bcba80` |
| `Farbexport.cs` | 7234 | `7bbd8fbd786c1a20b30c0d968a0842a430a9cdb66569a8a71fb8675c13b28b4d` |
| `Farbfolge.cs` | 17256 | `b4b009252864784117040b3865aa11d51d9fce925f0dbcd014a24afb65e12a1a` |
| `Farbglas.cs` | 30303 | `5b0c6d1cf48b91d26894947b6b648e88814ceff209d666bcce143c0c8742d12b` |
| `Farbstellungen/Anthrazit.xaml` | 42085 | `6f1af9c0d81ce26996581decfbb1fe9e87cf7c7b2b604b4d7d652ff45babfb1f` |
| `Farbstellungen/Standard.xaml` | 42668 | `199b35c07fe6d017b9473ad50bd939cb05c665cdd01ca10700cd879aef756560` |
| `Feld.cs` | 7558 | `576157a9a08c33febcf8653022a4c0c5a8a763ff0c7de533cb5a1b6be0499452` |
| `Fensterrahmen.cs` | 26512 | `4ae9e5293f2108459afb49f7a9aad55dd6b42d64471488b91ea374b389529497` |
| `Fluessigglas.cs` | 11848 | `4a19d37ad5eb3d934daffc31aaff288ce75134e792dfe34a6c85fb10f10879c7` |
| `Fokusring.cs` | 2582 | `731955d2b18e774ce8de08962423cdef9151648fc09c2dba32c61b8a65044a7f` |
| `Fortschritt.cs` | 27802 | `317d4c7d33a2bcb3b707cfc58585806fb14ba9835e043cb0b1de596ec92e0cf9` |
| `Fresnelrand.cs` | 26800 | `3dd57cf8586118a373498bd43d708a46357aa15c88799ca93214bc04e8a8ae44` |
| `Glanz.cs` | 54849 | `bf6030c822ce10ae5230203d284987ece8c6fc4776665efdeda6060e867db6c2` |
| `Glanzkugel.cs` | 22284 | `91086d812a08baf962ee10624ae32c8be64fa0953f737506f182fc1c95425778` |
| `Glanzwischer.cs` | 11661 | `233ee6feac1de619e243f504c0c28b6fe7add3410726077868c96a150b90aaf8` |
| `Glas.Daten.xaml` | 84712 | `b1988066b552cf7db2d92ad09dd53f69383ae9dfa6f1e23ce76bee4031e7b391` |
| `Glas.Eingabe.xaml` | 87305 | `132fd2a6c147b3814d9b5ce1c79c85ddf99ca5fe89e19384f9f3e8504efb9691` |
| `Glas.Familien.xaml` | 134222 | `4d6cfcb2f75f8db279829ddef03218186e508842e6b8f6016f2d2983d0f4279d` |
| `Glas.Navigation.xaml` | 78028 | `d34e3062cd8868937ec67a98894c1c27eb8a99e8a83957d9b81d2f77928bf066` |
| `Glas.Rueckmeldung.xaml` | 60666 | `dc40314c2258d6d5fad1755538c004edf656cb4e8ac6360bb9f0123097d268af` |
| `Glas.Stile.xaml` | 106796 | `994f4edf533c818b4b8d37d6b0b4c45de7575417088c9df4b30d913c4882e7a5` |
| `Glas.cs` | 21935 | `5771b597ff46e4e66d2c6dcc7e79875ebf8a0dec26244ecc71c205edc3d40ec4` |
| `Glas.xaml` | 1049 | `b5783c1a5b2d9e7d175fd4d07c3cd68169fb7ff412c3f4dc436b0c2b58919262` |
| `Glasdesign.csproj` | 875 | `7ab116865b737ee23d19e3bb83b0abfedefb3fa28ebe7903d0332573295ac0ed` |
| `Glasgeometrie.cs` | 14381 | `88e84b52c74d9f9abb2a8bec7cf750b55d6a388672c6e78b527604c03607e9e3` |
| `Glaskoerper.cs` | 20617 | `dce36080b0d9494eb57918e5721dcf1aeba306d3fa416b001eae3501daf65858` |
| `Glassymbol.cs` | 3910 | `f51f55b9551b1bd490035bbd20ecd68b65fda84fb0b85c1d2a9066d375fc5905` |
| `Glutglas.cs` | 27856 | `a1e07e0c4ee5854a6bca28900e63e857044d84bf0ed19b46a784c7cbaaa2b496` |
| `Hilfe.cs` | 6292 | `d0649bdad33385751851c3a80b78b2ae2fdcede386b03612be2c25cf8ba50459` |
| `Hinweisbanner.cs` | 13154 | `3221a31fb7a34c390b5ef60d433f27f454fe8b346138d94c5b7da1327743085b` |
| `Horizontglas.cs` | 12042 | `18b8136be07969125e38b9d8bf8050bc98e1932198e29885788c740ac9f1528c` |
| `Karte.cs` | 11152 | `fad009652e67cdb7e21bb2a7f9cf1b29ead60f520a5cfa120ab76e30de8e648d` |
| `Kontaktschatten.cs` | 3511 | `9d4ca061b2b1e715bf6101c23d7ef526f036e4f3ad4907a4ce91c45aace19f51` |
| `Ladeanzeige.cs` | 28500 | `8c4a45f025efa76a0001e3cd0e5270120c634450b1d730d7d215d345ff8a80fe` |
| `Leuchtgeometrie.cs` | 4934 | `3f15aa5174c0bf64b7f132cbf0f383e6585f52e95dab50da2d54f5692b30ef9e` |
| `Leuchtsaum.cs` | 14431 | `d7be4764a06e3eb5c0f483d698d3cbbfbf9b719fa11ce26a122c24ac8b4236e7` |
| `Leuchtschalter.cs` | 28776 | `1761bb4e801bb7db2c3d27aeae25fdef013dbdce4a5d2ce381b38ab699ed9a8e` |
| `Leuchtzeichen.cs` | 1234 | `b1883317b5af6d2fbd2d9fa845d474f94bb0efd14e388a21b8c29849a81cb921` |
| `Lichtkante.cs` | 12779 | `8b900f9a836e642df3558f4b7c551c508560a92adef6bf66a431f3e1232118b5` |
| `Listeneintrag.cs` | 3799 | `5779c6ab61053e94d5f4339c574cba6027222df60aeb5e682fe83e86cec76496` |
| `Matt.cs` | 10596 | `38291762f73e571a53935c57f24c3c461ad74abe1a4d3015398ce2d7d9413fa2` |
| `Milchglas.cs` | 11535 | `7439ea05ab7d4d4ccfd33a6f14baddc508a3ac8cf25fb8687b87c98cfbcc35e8` |
| `Plattenbild.cs` | 16942 | `cf879c73348f0e38a4157cc939f74c5a5e8a263f003fd7327d0058bd346a8726` |
| `Pultexport.cs` | 4762 | `5f8e7a4688b559b75cd4374f1e5ceca3239130c78d3672d286d9923d7565608f` |
| `Rundschatten.cs` | 9269 | `05f851755b0dc6755d21914f3e240e099acd91980b23de4e387e6f8e7137de55` |
| `Schalterbahn.cs` | 9781 | `41ab74f75d04ae1feda53f75ec2077710d0ccc3b911f6dfa7da388024156cc7c` |
| `Schaltergeometrie.cs` | 2541 | `a0704c9be281e9df4bca90a6c3150ed10b870f8a49787e94c0fd37e0a4b0eb36` |
| `Schluessel.cs` | 30846 | `a1ff1423fac6b00c633edf4fb0416ad1ad4eff272e15a0431710acc2510dfd33` |
| `Schrittfolge.cs` | 14393 | `7bcef4254acb18fddae61a64d35de52b723b9e2bb63ce2132ce496aa22eaea5c` |
| `Seitenwahl.cs` | 16855 | `8a987b7c9f47230b1faae12be46dc62da5e8063c7524feb7f2a8eeadd0291eb9` |
| `Spotlicht.cs` | 9285 | `d39f22383d8594ecbc4c74943ccb764b26ae01ee245972ba24f44dd6c2eaeb80` |
| `Sprechblase.cs` | 30725 | `fa83708a41ea368573e4d6abdf748f40f76a4a7595ccccbc3e11f933ac2e4bc4` |
| `Stilprofil.cs` | 9080 | `d1d2a6b07cfbb363a78e055eb34ef044d048cef1e943735a40a141d4cad0d282` |
| `Suchfeld.cs` | 22584 | `a44cf5b9b6e0b6edb19f106c7459447523930e67bde8b10552f73960bc8da943` |
| `Tabellenkopf.cs` | 17659 | `47a57fbbd54db05eb77ce2a519b92d4c33732a5ac5751d24f93c75418da38100` |
| `Taktil.cs` | 26432 | `be55c1877bdfc83305dd02f290c847c259e4172adf1c3f89f3b9a34c4bec3975` |
| `Teilvorrat.cs` | 3390 | `224b8daa94a3a7f12bf2ac9a23fce1d94cff94dbaa5a4ee68701535ccefe1178` |
| `Thema.cs` | 11511 | `2bfabdac69e9cd2a8aa66de589d7cc0392a5820b479f15f7f5d351dcfe4764e1` |
| `Toast.cs` | 16014 | `4a9dcc077784fbb214adaacf679c053ec7f57f4bd09895e5c34a402a95bc9368` |
| `Umriss.cs` | 23292 | `e7ebb7e46f7a96c93a7615fcc12605edf08a15b921d6942962cf10fcb0ddd516` |
| `Zahlenfeld.cs` | 6856 | `d3aaba3e7a5d901f2d408848f590d8fe0d179d78acc66f7a58f67482fe8e0a03` |
| `Zeichensatz.cs` | 10897 | `0dbcd7085c7eb4d7420a86a88d4db3e94e862ae1f8dae0ccdc97c27d29f0f224` |
| `Zeichner.cs` | 9600 | `fe69357246d2bc885ae03fda3843a513b120e58c1cce0589db3e95019abb1b9c` |
