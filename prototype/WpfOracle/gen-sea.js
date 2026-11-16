// 產生緬甸文、高棉文、藏文、寮文的測試字串（依音節結構隨機組合；不是真實語料）
let seed = Number(process.argv[3] ?? 20261116); const rnd = () => (seed = (seed * 1103515245 + 12345) % 2147483648) / 2147483648;
const pick = a => a[Math.floor(rnd() * a.length)];
const range = (a, b, ex = []) => { const r = []; for (let c = a; c <= b; c++) if (!ex.includes(c)) r.push(c); return r; };
const S = cps => String.fromCodePoint(...cps);
const out = { my: [], km: [], bo: [], lo: [] };
// 緬甸文
const myC = range(0x1000, 0x1021);
const myMed = [[], [0x103B], [0x103C], [0x103D], [0x103E], [0x103B, 0x103D], [0x103B, 0x103E], [0x103C, 0x103D], [0x103C, 0x103E], [0x103D, 0x103E]];
const myV = [[], [0x102C], [0x102B], [0x102D], [0x102E], [0x102F], [0x1030], [0x1031], [0x1032], [0x102D, 0x102F], [0x1031, 0x102C], [0x1031, 0x102C, 0x103A], [0x1031, 0x102B], [0x1036], [0x102D, 0x1036]];
const myF = [[], [0x1037], [0x1038], [0x1036, 0x1037]];
function mySyl() {
  let s = [pick(myC)];
  if (rnd() < 0.12) s.push(0x1039, pick(range(0x1000, 0x1020)));        // 疊字
  s.push(...pick(myMed)); const v = pick(myV); s.push(...v); const f = pick(myF); if (!(v.includes(0x103A) && f.includes(0x103A))) s.push(...f);
  if (rnd() < 0.25) { s.push(pick([0x1000, 0x1004, 0x1005, 0x100A, 0x1010, 0x1014, 0x1015, 0x1019]), 0x103A); if (rnd() < 0.4) s.push(pick([0x1037, 0x1038])); }
  if (rnd() < 0.05) s = [0x1004, 0x103A, 0x1039, ...s];                    // kinzi
  for (let i = 0; i + 1 < s.length; i++) if (s[i] === 0x103A && s[i + 1] === 0x1037) { s[i] = 0x1037; s[i + 1] = 0x103A; }   // ့ 在 ် 前面
  return s;
}
// 高棉文
const kmC = range(0x1780, 0x17A2);
const kmV = [[], ...range(0x17B6, 0x17C5).map(v => [v])];
const kmS = [[], [], [0x17C6], [0x17C7], [0x17CB], [0x17CE]];
function kmSyl() {
  const s = [pick(kmC)];
  if (rnd() < 0.3) s.push(0x17D2, pick(kmC));
  if (rnd() < 0.05) s.push(0x17D2, pick(kmC));
  if (rnd() < 0.15) s.push(pick([0x17C9, 0x17CA]));
  s.push(...pick(kmV)); s.push(...pick(kmS));
  return s;
}
// 藏文
const boC = range(0x0F40, 0x0F6A, [0x0F48]);
const boSub = range(0x0F90, 0x0FBC, [0x0F98]);
const boV = [[], [0x0F72], [0x0F74], [0x0F7A], [0x0F7C], [0x0F71], [0x0F71, 0x0F72], [0x0F80], [0x0F7E], [0x0F72, 0x0F7E]];
function boSyl() {
  const s = [];
  if (rnd() < 0.2) s.push(pick([0x0F42, 0x0F51, 0x0F56, 0x0F58, 0x0F60]));
  s.push(pick(boC));
  if (rnd() < 0.45) s.push(pick(boSub));
  if (rnd() < 0.1) s.push(pick(boSub));
  s.push(...pick(boV));
  if (rnd() < 0.4) s.push(pick([0x0F42, 0x0F44, 0x0F51, 0x0F53, 0x0F56, 0x0F58, 0x0F60, 0x0F62, 0x0F63, 0x0F66]));
  if (rnd() < 0.1) s.push(pick([0x0F66, 0x0F51]));
  return s;
}
// 寮文（邏輯順序跟泰文一樣：前置母音存在子音前面）
const loC = [0x0E81, 0x0E82, 0x0E84, 0x0E87, 0x0E88, 0x0E8A, 0x0E8D, ...range(0x0E94, 0x0E97), ...range(0x0E99, 0x0E9F), 0x0EA1, 0x0EA2, 0x0EA3, 0x0EA5, 0x0EA7, 0x0EAA, 0x0EAB, 0x0EAD, 0x0EAE];
function loSyl() {
  const s = [];
  const pre = rnd() < 0.25 ? pick([0x0EC0, 0x0EC1, 0x0EC2, 0x0EC3, 0x0EC4]) : null;
  if (pre) s.push(pre);
  s.push(pick(loC));
  if (rnd() < 0.08) s.push(0x0EBC);
  if (!pre && rnd() < 0.4) s.push(pick([0x0EB1, 0x0EB4, 0x0EB5, 0x0EB6, 0x0EB7, 0x0EB8, 0x0EB9, 0x0EBB]));   // 上下
  if (rnd() < 0.35) s.push(pick([0x0EC8, 0x0EC9, 0x0ECA, 0x0ECB]));                                        // 聲調
  if (rnd() < 0.3) s.push(pick([0x0EB0, 0x0EB2, 0x0EB3]));                                                 // 後面
  if (rnd() < 0.3) s.push(pick([0x0E81, 0x0E87, 0x0E8D, 0x0E94, 0x0E99, 0x0E9A, 0x0EA1, 0x0EA7]));
  return s;
}
const word = (syl, n, sep = []) => { const w = []; for (let i = 0; i < n; i++) { if (i) w.push(...sep); w.push(...syl()); } return S(w); };
for (let i = 0; i < 3000; i++) {
  const n = 1 + Math.floor(rnd() * 4);
  out.my.push(word(mySyl, n)); out.km.push(word(kmSyl, n)); out.bo.push(word(boSyl, n, [0x0F0B])); out.lo.push(word(loSyl, n));
}
require('fs').writeFileSync(process.argv[2], JSON.stringify(out));
console.log(Object.entries(out).map(([k, v]) => k + ':' + v.length).join(' '), out.my[0], out.km[0], out.bo[0], out.lo[0]);
