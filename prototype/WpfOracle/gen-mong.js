// 產生蒙古文測試字串（傳統蒙古文；語料裡沒有）：node gen-mong.js <out.json> [種子]
// 陽性／陰性母音和諧的字、MVS + a/e 結尾、窄不換行空白 + 詞尾、FVS、句子（空白、標點、數字）。
// 跟匯出時的自我檢查用不同的產生方式，避免拿同一種字驗自己。
const fs = require('fs');
let seed = Number(process.argv[3] || 5);
const rnd = () => { seed = (seed * 1103515245 + 12345) % 2147483648; return seed / 2147483648; };
const pick = a => a[Math.floor(rnd() * a.length)];
const c = cp => String.fromCodePoint(cp);
const masc = [0x1820, 0x1823, 0x1824], fem = [0x1821, 0x1825, 0x1826], neutral = [0x1822];
const cons = []; for (let cp = 0x1828; cp <= 0x1842; cp++) cons.push(cp);
const all = []; for (let cp = 0x1820; cp <= 0x1878; cp++) all.push(cp);
const fvs = [0x180B, 0x180C, 0x180D];
function word() {
  const vowels = rnd() < 0.5 ? masc.concat(neutral) : fem.concat(neutral);
  let w = '';
  const syl = 1 + Math.floor(rnd() * 4);
  for (let s = 0; s < syl; s++) {
    if (rnd() < 0.8) w += c(pick(cons));
    w += c(pick(vowels));
    if (rnd() < 0.4) w += c(pick(cons));
    if (rnd() < 0.08) w += c(pick(fvs));
  }
  if (rnd() < 0.1) w += '\u180E' + c(vowels[0] === 0x1820 ? 0x1820 : 0x1821);
  if (rnd() < 0.15) { w += '\u202F'; for (let k = 1 + Math.floor(rnd() * 3); k > 0; k--) w += c(rnd() < 0.5 ? pick(vowels) : pick(cons)); }
  if (rnd() < 0.05) w = c(pick(all)) + w;
  return w;
}
const out = [];
for (let n = 0; n < 3000; n++) out.push(word());
for (let n = 0; n < 1000; n++) {
  const k = 2 + Math.floor(rnd() * 5);
  let s = '';
  for (let i = 0; i < k; i++) s += (i ? ' ' : '') + word() + (rnd() < 0.1 ? pick(['\u1802', '\u1803', ',', '.', ':']) : '');
  if (rnd() < 0.1) s += ' ' + c(0x1810 + Math.floor(rnd() * 10)) + c(0x1810 + Math.floor(rnd() * 10));
  out.push(s);
}
fs.writeFileSync(process.argv[2], JSON.stringify({ mn: out }));
console.log('mn:' + out.length);
