// 把 prototype/ThemeChrome 的三份輸出合成 data/theme-chrome.json
const fs = require('fs');
const [, , dir, out] = process.argv;
const meta = {
  mahapps: { id: 'MahApps.Metro 2.4', package: 'MahApps.Metro', assemblies: ['MahApps.Metro'], namespaces: ['http://metro.mahapps.com/winfx/xaml/controls'], requires: null },
  md2: { id: 'MaterialDesignThemes 5.3 (MaterialDesign2.Defaults)', package: 'MaterialDesignThemes', assemblies: ['MaterialDesignThemes.Wpf', 'MaterialDesignColors'], namespaces: ['http://materialdesigninxaml.net/winfx/xaml/themes'], requires: 'MaterialDesign2.Defaults.xaml' },
  md3: { id: 'MaterialDesignThemes 5.3 (MaterialDesign3.Defaults)', package: 'MaterialDesignThemes', assemblies: ['MaterialDesignThemes.Wpf', 'MaterialDesignColors'], namespaces: ['http://materialdesigninxaml.net/winfx/xaml/themes'], requires: 'MaterialDesign3.Defaults.xaml' },
};
const keep = ['targetType', 'followsSystem', 'chromeWidth', 'padding', 'border', 'paddingMode', 'borderMode', 'minWidth', 'fontFamily', 'fontSize', 'fontWeight', 'casing', 'textFontFamily', 'textFontSize', 'textFontWeight'];
function clean(v) {
  const o = {};
  for (const k of keep) if (v[k] !== undefined) o[k] = v[k];
  if (o.paddingMode && !['replace', 'ignore'].includes(o.paddingMode)) delete o.paddingMode;   // 量出來不是一比一：工具保守地加上去
  if (o.borderMode && !['replace', 'ignore'].includes(o.borderMode)) delete o.borderMode;
  if (o.casing && !['as-is', 'upper', 'lower'].includes(o.casing)) delete o.casing;
  return o;
}
const themes = [];
for (const name of ['mahapps', 'md2', 'md3']) {
  const j = JSON.parse(fs.readFileSync(`${dir}/${name}.json`, 'utf8'));
  const controls = {}, styles = {};
  for (const [k, v] of Object.entries(j.controls)) if (!v.failed) controls[k] = clean(v);
  for (const [k, v] of Object.entries(j.styles)) if (!v.failed) styles[k] = clean(v);
  themes.push({ ...meta[name], version: j.version, dictionaries: j.dictionaries, controls, styles });
}
const file = {
  schemaVersion: 1,
  generatedOn: new Date().toISOString().slice(0, 10),
  note: 'Horizontal chrome, fonts and text casing of standard WPF controls under control-library themes, measured with WPF by prototype/ThemeChrome: ' +
        "the theme's dictionaries merged into Application.Resources as a project would, then the same probes as ChromeGen. " +
        'chromeWidth includes the style\'s Padding and BorderThickness; paddingMode/borderMode say what an explicit value does (absent = not one-for-one, added on top). ' +
        'fontFamily/fontSize/fontWeight are listed only when the style sets them. casing is what the style does to string content. ' +
        'A profile applies only when the project references the same package major.minor. Numbers and style keys only; no theme XAML is copied.',
  themes,
};
fs.writeFileSync(out, JSON.stringify(file, null, 1) + '\n');
console.log(themes.map(t => `${t.id} ${t.version}: ${Object.keys(t.controls).length} controls, ${Object.keys(t.styles).length} styles`).join('\n'));
