# Evidence from running apps

Screenshots of real applications showing text that XamlFit reported, and `prototype/CorpusRender`
confirmed, as clipped. They are linked from the issues reported upstream.

| File | What it shows |
|---|---|
| `playnite-10.64-bg-library-manager.png` | Playnite 10.64 (portable, Bulgarian, 125% scaling), Library manager: the bottom-right button “Премахване на неизползваните” (Remove unused) is cut off — `DatabaseFieldsManagerWindow.xaml:34`, predicted 205 DIP needed, 127 available. |
| `playnite-10.64-bg-software-tools.png` | Same, Software tools: the third button of the bottom-left row, “Премахване” (Remove), is pushed out of view — `ToolsConfigWindow.xaml:52`, predicted visible width 0. |
