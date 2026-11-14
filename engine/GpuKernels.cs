namespace PixTune
{
    // The tools' per-pixel shaders. Each one is the line-by-line translation of the CPU loop it replaces (named in
    // its comment), with the same helpers as Ops (Core.cs), so both give the same pixels; tests\GpuToolTest.ps1
    // compares them tool by tool. Pictures are three planes: R, G, B in In0, In1, In2 (outputs Out0..Out2).
    public static partial class Gpu
    {
        const string ToolHlsl = @"
// ---------- helpers: copies of Ops ----------
float C01(float v) { return v < 0 ? 0 : (v > 1 ? 1 : v); }
float Lerp1(float a, float b, float t) { return a + (b - a) * t; }
float3 Lerp3(float3 a, float3 b, float t) { return a + (b - a) * t; }
float SStep(float e0, float e1, float x) { float t = C01((x - e0) / (e1 - e0)); return t * t * (3 - 2 * t); }
float Lum(float3 c) { return 0.299 * c.r + 0.587 * c.g + 0.114 * c.b; }
float Clip(float v, float lim) { return v / (1 + abs(v) / lim); }
float Screen(float a, float b) { return 1 - (1 - a) * (1 - b); }
float SoftLight(float a, float b) { return b < 0.5 ? a - (1 - 2 * b) * a * (1 - a) : a + (2 * b - 1) * ((a < 0.25 ? ((16 * a - 12) * a + 4) * a : sqrt(a)) - a); }
float PowZ(float x, float g) { return pow(max(0, x), g); }
float Contrast(float x, float c) {
    if (c == 0) return x;
    float xc = C01(x);
    float d = (xc - 0.5) * (1 - abs(2 * xc - 1));
    float y = xc + 0.95 * c * d * 2;
    if (c > 0) { float d2 = (y - 0.5) * (1 - abs(2 * C01(y) - 1)); y = y + 0.95 * c * d2 * 0.9; }
    return y + (x - xc);
}
float3 SetLum(float3 c, float l0, float l1) {
    if (l1 <= l0) { float k = l0 > 1e-5 ? l1 / l0 : 0; return c * k; }
    float k2 = l0 > 1e-4 ? l1 / l0 : 1e4;
    float3 cc = c * k2;
    float mx = max(cc.r, max(cc.g, cc.b));
    if (mx > 1 || l0 < 0.02) {
        float3 a = c + (l1 - l0);
        float t = l0 < 0.02 ? 1 : C01((mx - 1) * 2);
        cc = cc + (a - cc) * t;
    }
    return cc;
}
float3 Sat3(float3 c, float f) { float l = Lum(c); return l + (c - l) * f; }
float3 Warm(float3 c, float wa) { float l0 = Lum(c); c.r += wa * 0.09; c.b -= wa * 0.1; float d = l0 - Lum(c); return c + d; }

uint HashU(int x, int y, int seed) { uint h = (uint)x * 374761393u + (uint)y * 668265263u + (uint)seed * 1442695041u; h = (h ^ (h >> 13)) * 1274126177u; return h ^ (h >> 16); }
float HashT(int x, int y, int seed) { return (HashU(x, y, seed) & 0xFFFFFF) / 16777215.0; }
float VNoise(float x, float y, int seed) {
    int xi = (int)floor(x), yi = (int)floor(y);
    float fx = x - xi, fy = y - yi;
    fx = fx * fx * (3 - 2 * fx); fy = fy * fy * (3 - 2 * fy);
    float a = HashT(xi, yi, seed), b = HashT(xi + 1, yi, seed), c = HashT(xi, yi + 1, seed), d = HashT(xi + 1, yi + 1, seed);
    return (a * (1 - fx) + b * fx) * (1 - fy) + (c * (1 - fx) + d * fx) * fy;
}
float Fbm(float x, float y, int seed, int oct) {
    float a = 0.5, s = 0, f = 1, norm = 0;
    [loop] for (int k = 0; k < oct; k++) { s += a * VNoise(x * f, y * f, seed + k * 31); norm += a; f *= 2.03; a *= 0.5; }
    return s / norm;
}

#define RGBIN float3 c = float3(In0[i], In1[i], In2[i]);
#define RGBOUT(v) Out0[i] = (v).r; Out1[i] = (v).g; Out2[i] = (v).b;

// ---------- moving pictures: interleaved rows <-> planes (I0: first pixel of the rows) ----------
[numthreads(16,16,1)] void Deint(uint3 id : SV_DispatchThreadID) { GUARD Out0[I0 + i] = In0[i * 3]; Out1[I0 + i] = In0[i * 3 + 1]; Out2[I0 + i] = In0[i * 3 + 2]; }
[numthreads(16,16,1)] void Inter(uint3 id : SV_DispatchThreadID) { GUARD Out0[i * 3] = In0[I0 + i]; Out0[i * 3 + 1] = In1[I0 + i]; Out0[i * 3 + 2] = In2[I0 + i]; }

// ---------- the end of every tool (Pipeline.Apply) and the frame shown (Img.ToBitmap) ----------
[numthreads(16,16,1)] void Clamp3(uint3 id : SV_DispatchThreadID) { GUARD RGBIN RGBOUT(float3(C01(c.r), C01(c.g), C01(c.b))) }
// In0..In2 the tool's input, In3..In5 its result, In6 the mask
[numthreads(16,16,1)] void MaskMix(uint3 id : SV_DispatchThreadID) {
    GUARD
    RGBIN
    float t = In6[i];
    float3 b = float3(In3[i], In4[i], In5[i]);
    RGBOUT(c + (b - c) * t)
}
// Ops.B per channel, packed as blue | green << 8 | red << 16 in a float (exact below 2^24)
uint B8(float v) { int q = (int)(v * 255.0 + 0.5); return (uint)(q < 0 ? 0 : (q > 255 ? 255 : q)); }
[numthreads(16,16,1)] void Pack(uint3 id : SV_DispatchThreadID) { GUARD Out0[i] = (float)(B8(In2[i]) | (B8(In1[i]) << 8) | (B8(In0[i]) << 16)); }

// ---------- plane operations ----------
// Ops.DownBox: s x s block averages (PI(0) = s) of a PI(1) x PI(2) plane, the same order of sums as the CPU;
// PI(3) = 1 averages the squared values
[numthreads(16,16,1)] void DownBox(uint3 id : SV_DispatchThreadID) {
    GUARD
    int s = PI(0), w0 = PI(1), h0 = PI(2), sq = PI(3);
    int x0 = (int)id.x * s, y0 = (int)id.y * s, x1 = min(x0 + s, w0), y1 = min(y0 + s, h0);
    float sum = 0; int n = 0;
    for (int yy = y0; yy < y1; yy++) for (int xx = x0; xx < x1; xx++) { float v = In0[yy * w0 + xx]; sum += sq == 1 ? v * v : v; n++; }
    Out0[i] = sum / n;
}
[numthreads(16,16,1)] void Luma(uint3 id : SV_DispatchThreadID) { GUARD Out0[i] = 0.299 * In0[i] + 0.587 * In1[i] + 0.114 * In2[i]; }
[numthreads(16,16,1)] void Scale(uint3 id : SV_DispatchThreadID) { GUARD Out0[i] = In0[i] * PF(0); }
[numthreads(16,16,1)] void Mix2(uint3 id : SV_DispatchThreadID) { GUARD Out0[i] = PF(0) * In0[i] + PF(1) * In1[i]; }
[numthreads(16,16,1)] void LogP(uint3 id : SV_DispatchThreadID) { GUARD Out0[i] = log(In0[i] + PF(0)); }
// Ops.SamplePlane over a resized plane: source I0 x I1, steps A, B
[numthreads(16,16,1)] void Sample(uint3 id : SV_DispatchThreadID) {
    GUARD
    int w = I0, h = I1;
    float fx = ((int)id.x + 0.5) * A - 0.5, fy = ((int)id.y + 0.5) * B - 0.5;
    if (fx < 0) fx = 0; else if (fx > w - 1) fx = w - 1;
    if (fy < 0) fy = 0; else if (fy > h - 1) fy = h - 1;
    int x0 = (int)fx, y0 = (int)fy;
    int x1 = x0 + 1 < w ? x0 + 1 : x0, y1 = y0 + 1 < h ? y0 + 1 : y0;
    float ax = fx - x0, ay = fy - y0;
    Out0[i] = (In0[y0 * w + x0] * (1 - ax) + In0[y0 * w + x1] * ax) * (1 - ay) + (In0[y1 * w + x0] * (1 - ax) + In0[y1 * w + x1] * ax) * ay;
}

// ---------- Adjust.ToneCore (Tune, Raw Develop). In3: blurred luma Lb when PI(0) = 1 ----------
[numthreads(16,16,1)] void ToneCore(uint3 id : SV_DispatchThreadID) {
    GUARD
    float br = PF(0), am = PF(1), co = PF(2), sa = PF(3), sh = PF(4), hi = PF(5), wa = PF(6), expo = PF(7), gamma = PF(8), ek = PF(9), vi = PF(10);
    float3 c = float3(In0[i], In1[i], In2[i]) * ek;
    float l = Lum(c);
    float t = l;
    if (expo > 0 && t > 0.8) t = 0.8 + Clip(t - 0.8, 0.2);
    if (PI(0) == 1) {
        float lb = In3[i] * (expo != 0 ? ek : 1);
        if (sh != 0) { float ws = SStep(0.7, 0.0, lb); t += sh * 0.42 * ws * (sh > 0 ? (1 - t) * (0.35 + 0.65 * (1 - t)) : t); }
        if (hi != 0) { float wh = SStep(0.3, 1.0, lb); t += hi * 0.38 * wh * (hi > 0 ? (1 - t) : t); }
        if (am != 0) { float bal = 0.5 - lb; t += am * 0.32 * bal * (1 - abs(2 * t - 1) * 0.6); t += am * 0.6 * (l - lb) * 0.5; }
    }
    if (br != 0) t = pow(max(0, t), gamma);
    if (co != 0) t = Contrast(t, co);
    if (t < 0) t = 0;
    c = SetLum(c, l, t);
    float f = 1 + sa + am * 0.35;
    if (f != 1) {
        float mx = max(c.r, max(c.g, c.b)), mn = min(c.r, min(c.g, c.b));
        float cs = mx > 1e-4 ? (mx - mn) / mx : 0;
        float ff = f > 1 ? 1 + (f - 1) * (1 - 0.55 * cs) : f;
        c = Sat3(c, ff);
    }
    if (vi != 0) {
        float mx = max(c.r, max(c.g, c.b)), mn = min(c.r, min(c.g, c.b));
        float cs = mx > 1e-4 ? (mx - mn) / mx : 0;
        float wgt = (1 - cs) * (1 - cs);
        if (c.r > c.g && c.g > c.b && mx > 0.15) {
            float t2 = c.r - c.b > 1e-4 ? (c.g - c.b) / (c.r - c.b) : 0;
            wgt *= 1 - 0.7 * (1 - min(1.0, abs(t2 - 0.5) / 0.3));
        }
        c = Sat3(c, 1 + vi * wgt);
    }
    if (wa != 0) {
        float l0 = Lum(c);
        c.r += wa * 0.10 * (0.4 + l0); c.g += wa * 0.018; c.b -= wa * 0.11 * (0.4 + l0);
        c += l0 - Lum(c);
    }
    RGBOUT(c)
}

// ---------- Adjust.DetailsCore. In3 L, In4 b1, In5 b2 (PI(0) = 1), In6 bs (PI(1) = 1) ----------
[numthreads(16,16,1)] void Details(uint3 id : SV_DispatchThreadID) {
    GUARD
    float st = PF(0), sh = PF(1);
    float l = In3[i], t = l;
    if (PI(0) == 1) {
        if (st > 0) { float d = (l - In4[i]) * 0.7 + (l - In5[i]) * 0.5; t += Clip(d * st * 2.6, 0.25); }
        else if (st < 0) t = l + (In4[i] - l) * (-st);
    }
    if (PI(1) == 1) t += Clip((l - In6[i]) * sh * 2.4, 0.2);
    RGBIN
    RGBOUT(c + (t - l))
}

// ---------- Adjust.Curves. In3: 5 tables of 1024 (rgb, r, g, b, luminance), PI(0): which are used ----------
float LutAt(int k, float v) {
    int n = 1023;
    float f = C01(v) * n;
    int ii = (int)f;
    if (ii >= n) return In3[k * 1024 + n];
    float a = f - ii;
    return In3[k * 1024 + ii] * (1 - a) + In3[k * 1024 + ii + 1] * a;
}
[numthreads(16,16,1)] void Curves(uint3 id : SV_DispatchThreadID) {
    GUARD
    int m = PI(0);
    RGBIN
    if (m & 1) { c.r = LutAt(0, c.r); c.g = LutAt(0, c.g); c.b = LutAt(0, c.b); }
    if (m & 2) c.r = LutAt(1, c.r);
    if (m & 4) c.g = LutAt(2, c.g);
    if (m & 8) c.b = LutAt(3, c.b);
    if (m & 16) { float l = Lum(c); c = SetLum(c, l, LutAt(4, l)); }
    RGBOUT(c)
}

// ---------- Adjust.WbCore ----------
[numthreads(16,16,1)] void Wb(uint3 id : SV_DispatchThreadID) { GUARD RGBIN RGBOUT(c * float3(PF(0), PF(1), PF(2))) }

// ---------- Adjust.Tonal. In3 L, In4 b1, In5 b2, In6 b3 ----------
[numthreads(16,16,1)] void Tonal(uint3 id : SV_DispatchThreadID) {
    GUARD
    float hi = PF(0), mid = PF(1), lo = PF(2), ps = PF(3), ph = PF(4);
    float l = In3[i], t = In5[i];
    float wl = (1 - t) * (1 - t), wh = t * t, wm = 4 * t * (1 - t);
    float amt = hi * wh * 1.4 + mid * wm + lo * wl * 1.4;
    float d = (l - In4[i]) * 0.6 + (l - In5[i]) * 1.0 + (l - In6[i]) * 0.5;
    float prot = 1 - ps * SStep(0.25, 0.0, l) - ph * SStep(0.75, 1.0, l);
    float nl = l + Clip(d * amt * 3.2 * max(0, prot), 0.3);
    RGBIN
    RGBOUT(c + (nl - l))
}

// ---------- Adjust.Drama. In3 L, In4 bl, In5 bl2 ----------
[numthreads(16,16,1)] void Drama(uint3 id : SV_DispatchThreadID) {
    GUARD
    float amt = PF(0), f = PF(1), tn = PF(2), satk = PF(3);
    float l = In3[i];
    float d = (l - In4[i]) + (l - In5[i]) * 0.6;
    float t = l + Clip(d * amt * f * 4.2, 0.35);
    t = C01(t);
    if (tn > 0) t = 1 - pow(1 - t, 1 + tn * 2.5);
    else if (tn < 0) t = pow(t, 1 - tn * 2.5);
    RGBIN
    c = SetLum(c, l, t);
    RGBOUT(Sat3(c, satk))
}

// ---------- Adjust.Hdr. In3 L, In4 log luma, In5 its smoothed base ----------
[numthreads(16,16,1)] void Hdr(uint3 id : SV_DispatchThreadID) {
    GUARD
    float mn = PF(0), cmp = PF(1), dt = PF(2), f = PF(3), br = PF(4), gamma = PF(5), satk = PF(6);
    float bl = In5[i], lg = In4[i];
    float nl = mn + (bl - mn) * (1 - cmp) + (lg - bl) * (1 + dt) + cmp * 0.12;
    float t = exp(nl) - 0.02;
    t = Lerp1(t, Contrast(C01(t), 0.25 * f), 0.6);
    if (br != 0) t = pow(max(0, t), gamma);
    RGBIN
    c = SetLum(c, In3[i], C01(t));
    RGBOUT(Sat3(c, satk))
}

// ---------- Adjust.Glamour. In3..In5 the blurred picture ----------
[numthreads(16,16,1)] void Glamour(uint3 id : SV_DispatchThreadID) {
    GUARD
    float k = PF(0), sa = PF(1), wa = PF(2);
    RGBIN
    float3 bb = float3(In3[i], In4[i], In5[i]);
    float3 s = float3(Screen(c.r, bb.r * 0.9), Screen(c.g, bb.g * 0.9), Screen(c.b, bb.b * 0.9));
    // Ops.SoftLight(s, bb) per channel
    float3 sl;
    sl.r = bb.r < 0.5 ? s.r - (1 - 2 * bb.r) * s.r * (1 - s.r) : s.r + (2 * bb.r - 1) * ((s.r < 0.25 ? ((16 * s.r - 12) * s.r + 4) * s.r : sqrt(s.r)) - s.r);
    sl.g = bb.g < 0.5 ? s.g - (1 - 2 * bb.g) * s.g * (1 - s.g) : s.g + (2 * bb.g - 1) * ((s.g < 0.25 ? ((16 * s.g - 12) * s.g + 4) * s.g : sqrt(s.g)) - s.g);
    sl.b = bb.b < 0.5 ? s.b - (1 - 2 * bb.b) * s.b * (1 - s.b) : s.b + (2 * bb.b - 1) * ((s.b < 0.25 ? ((16 * s.b - 12) * s.b + 4) * s.b : sqrt(s.b)) - s.b);
    s = Lerp3(s, sl, 0.5);
    c = Lerp3(c, s, k);
    c = Sat3(c, 1 + sa);
    if (wa != 0) c = Warm(c, wa);
    RGBOUT(c)
}

// ---------- Adjust.Vignette: centre PF(0), PF(1) in pixels, radius PF(2), outer PF(3), inner PF(4) ----------
[numthreads(16,16,1)] void Vignette(uint3 id : SV_DispatchThreadID) {
    GUARD
    float dx = (int)id.x - PF(0), dy = (int)id.y - PF(1);
    float d = sqrt(dx * dx + dy * dy) / PF(2);
    float m = SStep(0.45, 1.35, d);
    float k = pow(2.0, PF(3) * 1.6 * m + PF(4) * 0.9 * (1 - m));
    RGBIN
    RGBOUT(c * k)
}

// ---------- Style.VignetteMul and the film vignette: 1 - amount * smoothstep(e0, e1, distance / hd) ----------
[numthreads(16,16,1)] void VigMul(uint3 id : SV_DispatchThreadID) {
    GUARD
    float dx = (int)id.x - PF(0), dy = (int)id.y - PF(1);
    float k = 1 - PF(3) * SStep(PF(4), PF(5), sqrt(dx * dx + dy * dy) / PF(2));
    RGBIN
    RGBOUT(c * k)
}

// ---------- Style.Vintage's soft edges: blend towards the blurred picture (In3..In5) away from the centre ----------
[numthreads(16,16,1)] void VintageBlur(uint3 id : SV_DispatchThreadID) {
    GUARD
    float dx = (int)id.x - PF(0), dy = (int)id.y - PF(1);
    float a = SStep(0.25, 0.9, sqrt(dx * dx + dy * dy) / PF(2)) * PF(3);
    RGBIN
    RGBOUT(Lerp3(c, float3(In3[i], In4[i], In5[i]), a))
}

// ---------- Grade.Apply (Style.cs). In3: Sh(3), Hi(3), Fade, Sat, Con, Bright, 2^-Bright ----------
float3 GradeApply(float3 c, float k) {
    float l = Lum(c);
    float t = SStep(0, 1, l);
    float3 o = c;
    c.r += Lerp1(In3[0], In3[3], t) * 1.6; c.g += Lerp1(In3[1], In3[4], t) * 1.6; c.b += Lerp1(In3[2], In3[5], t) * 1.6;
    float fade = In3[6], sat = In3[7], con = In3[8], bright = In3[9], gm = In3[10];
    if (bright != 0) { c.r = PowZ(c.r, gm); c.g = PowZ(c.g, gm); c.b = PowZ(c.b, gm); }
    if (con != 0) { c.r = Contrast(c.r, con); c.g = Contrast(c.g, con); c.b = Contrast(c.b, con); }
    c = Sat3(c, sat);
    if (fade > 0) c = fade + c * (1 - fade * 0.85);
    return Lerp3(o, c, k);
}

// Grainy (PI(0) = 0) and Vintage (PI(0) = 1: brightness PF(1) when PI(1) = 1, saturation PF(2), paper texture of
// scale PF(3), seed PI(2), at the photo's place PI(3), PI(4))
[numthreads(16,16,1)] void Grade(uint3 id : SV_DispatchThreadID) {
    GUARD
    float k = PF(0);
    RGBIN
    c = GradeApply(c, k);
    if (PI(0) == 1) {
        if (PI(1) == 1) { float g = PF(1); c.r = PowZ(c.r, g); c.g = PowZ(c.g, g); c.b = PowZ(c.b, g); }
        c = Sat3(c, 1 + PF(2));
        float sc = PF(3);
        float tx = (Fbm(((int)id.x + PI(3)) / (40.0 * sc), ((int)id.y + PI(4)) / (40.0 * sc), PI(2), 4) - 0.5) * 0.06 * k;
        c += float3(tx, tx, tx * 0.6);
    }
    RGBOUT(c)
}

// ---------- Film.ApplyFilm's colour part. In3: grade (11 values), then mix (9) at 11, mono mix (3) at 20 ----------
[numthreads(16,16,1)] void FilmColor(uint3 id : SV_DispatchThreadID) {
    GUARD
    float k = PF(0), gamma = PF(1), wa = PF(2);
    RGBIN
    float3 c0 = c;
    if (PI(0) == 1) {
        float nr = In3[11] * c.r + In3[12] * c.g + In3[13] * c.b, ng = In3[14] * c.r + In3[15] * c.g + In3[16] * c.b, nb = In3[17] * c.r + In3[18] * c.g + In3[19] * c.b;
        c = float3(nr, ng, nb);
    }
    if (PI(1) == 1) { float v = c.r * In3[20] + c.g * In3[21] + c.b * In3[22]; c = float3(v, v, v); }
    c = GradeApply(c, 1);
    c.r = Lerp1(c.r, SStep(-0.05, 1.05, c.r), 0.25); c.g = Lerp1(c.g, SStep(-0.05, 1.05, c.g), 0.25); c.b = Lerp1(c.b, SStep(-0.05, 1.05, c.b), 0.25);
    c = Lerp3(c0, c, k);
    if (PI(2) == 1) { c.r = PowZ(c.r, gamma); c.g = PowZ(c.g, gamma); c.b = PowZ(c.b, gamma); }
    if (wa != 0) c = Warm(c, wa);
    RGBOUT(c)
}

// ---------- Film.Glow: bright parts (tinted when PI(0) = 1), halation edges, screen blend ----------
[numthreads(16,16,1)] void GlowBright(uint3 id : SV_DispatchThreadID) {
    GUARD
    RGBIN
    float l = Lum(c);
    float k = SStep(PF(0), PF(1), l);
    float3 o = PI(0) == 1 ? l * k * float3(PF(2), PF(3), PF(4)) : c * k;
    RGBOUT(o)
}
[numthreads(16,16,1)] void GlowEdge(uint3 id : SV_DispatchThreadID) {
    GUARD
    RGBIN
    RGBOUT(max(0, c * 1.6 - float3(In3[i], In4[i], In5[i]) * 0.8))
}
[numthreads(16,16,1)] void GlowScreen(uint3 id : SV_DispatchThreadID) {
    GUARD
    float s = PF(0);
    RGBIN
    float3 g = float3(In3[i], In4[i], In5[i]);
    RGBOUT(float3(Screen(C01(c.r), C01(g.r * s)), Screen(C01(c.g), C01(g.g * s)), Screen(C01(c.b), C01(g.b * s))))
}

// ---------- Style/Film AddGrain: In3 grain, In4 colour grain (PI(0) = 1), amount PF(0) ----------
[numthreads(16,16,1)] void GrainAdd(uint3 id : SV_DispatchThreadID) {
    GUARD
    float amount = PF(0);
    RGBIN
    float l = Lum(c);
    float wgt = 0.35 + 2.6 * l * (1 - l);
    float n = In3[i] * amount * 0.09 * wgt;
    float cn = PI(0) == 1 ? In4[i] * amount * 0.02 * wgt : 0;
    RGBOUT(float3(c.r + n + cn, c.g + n, c.b + n - cn))
}

// ---------- Style.BlackWhite: filter weights PF(0..2), gamma PF(3), contrast PF(4), sky darkening PI(0) ----------
[numthreads(16,16,1)] void BlackWhite(uint3 id : SV_DispatchThreadID) {
    GUARD
    RGBIN
    float v = c.r * PF(0) + c.g * PF(1) + c.b * PF(2);
    if (PI(0) == 1) { float bl = C01((c.b - (c.r + c.g) * 0.5) * 3); v *= 1 - 0.45 * bl; }
    v = pow(max(0, v), PF(3));
    v = Contrast(v, PF(4));
    RGBOUT(float3(v, v, v))
}

// ---------- Style.Noir: filter PF(0..2), gamma PF(3), contrast PF(4), mix PF(5), warm PF(6) ----------
[numthreads(16,16,1)] void Noir(uint3 id : SV_DispatchThreadID) {
    GUARD
    RGBIN
    float v = c.r * PF(0) + c.g * PF(1) + c.b * PF(2);
    float l0 = v;
    v = pow(max(0, v), PF(3));
    v = Contrast(v, PF(4));
    v = Lerp1(l0, v, PF(5));
    RGBOUT(float3(v + PF(6), v, v - PF(6)))
}
// Noir's wash: towards a screen with the blurred picture (In3..In5)
[numthreads(16,16,1)] void WashScreen(uint3 id : SV_DispatchThreadID) {
    GUARD
    RGBIN
    float3 b = float3(In3[i], In4[i], In5[i]) * 0.85;
    float w = PF(0);
    RGBOUT(float3(Lerp1(c.r, Screen(c.r, b.r), w), Lerp1(c.g, Screen(c.g, b.g), w), Lerp1(c.b, Screen(c.b, b.b), w)))
}
// Noir's light film damage: smooth noise of scale PF(0), amount PF(1), seed PI(0), at the photo's place PI(1), PI(2)
[numthreads(16,16,1)] void FbmAdd(uint3 id : SV_DispatchThreadID) {
    GUARD
    float sc = PF(0);
    float n = Fbm(((int)id.x + PI(1)) / (120.0 * sc), ((int)id.y + PI(2)) / (120.0 * sc), PI(0), 4) - 0.5;
    RGBIN
    RGBOUT(c + n * PF(1))
}

// ---------- Film.ColorBands. In3: hue shifts (8), saturation (8), luminance (8) ----------
static const float centers[8] = { 0, 30, 60, 120, 180, 230, 275, 320 };
[numthreads(16,16,1)] void ColorBands(uint3 id : SV_DispatchThreadID) {
    GUARD
    RGBIN
    float r = C01(c.r), g = C01(c.g), b = C01(c.b);
    float mx = max(r, max(g, b)), mn = min(r, min(g, b)), d = mx - mn;
    float vv = mx, ss = mx > 1e-5 ? d / mx : 0, hh = 0;
    if (d >= 1e-5) {
        if (mx == r) hh = 60 * fmod((g - b) / d, 6);
        else if (mx == g) hh = 60 * ((b - r) / d + 2);
        else hh = 60 * ((r - g) / d + 4);
        if (hh < 0) hh += 360;
    }
    if (ss < 0.02) { RGBOUT(c) return; }
    float sh = 0, sf = 0, sl = 0, wsum = 0;
    [unroll] for (int k = 0; k < 8; k++) {
        float dd = abs(hh - centers[k]); if (dd > 180) dd = 360 - dd;
        float half = k < 3 ? 30 : 45;
        float wg = max(0, 1 - dd / half);
        wg = wg * wg * (3 - 2 * wg);
        if (wg > 0) { sh += In3[k] * wg; sf += In3[8 + k] * wg; sl += In3[16 + k] * wg; wsum += wg; }
    }
    if (wsum <= 0) { RGBOUT(c) return; }
    float kk = min(1, ss * 2.5);
    float ns = C01(ss * (1 + sf * kk));
    float h = hh + sh * kk;
    h = fmod(fmod(h, 360) + 360, 360);
    float cc = vv * ns, x = cc * (1 - abs(fmod(h / 60, 2) - 1)), m = vv - cc;
    float3 o;
    if (h < 60) o = float3(cc, x, 0); else if (h < 120) o = float3(x, cc, 0);
    else if (h < 180) o = float3(0, cc, x); else if (h < 240) o = float3(0, x, cc);
    else if (h < 300) o = float3(x, 0, cc); else o = float3(cc, 0, x);
    o += m;
    float l0 = Lum(o);
    o = SetLum(o, l0, max(0, l0 * (1 + sl * 0.6 * kk)));
    RGBOUT(o)
}

// ---------- Style.Grunge. PF: 0 contrast, 1 desaturation, 2 tr, 3 tg, 4 tb, 5 texture amount, 6 scale,
// 7 half-diagonal, 8 centre x, 9 centre y, 10 gamma. PI: 0 texture, 1 noise seed, 2 speck size, 3 use gamma ----------
[numthreads(16,16,1)] void Grunge(uint3 id : SV_DispatchThreadID) {
    GUARD
    int tex = PI(0), ts0 = PI(1), speck = PI(2);
    float sc = PF(6), per = 90.0 * sc;
    RGBIN
    float l = Lum(c);
    c = float3(Contrast(c.r, PF(0)), Contrast(c.g, PF(0)), Contrast(c.b, PF(0)));
    c = Sat3(c, PF(1));
    float t = SStep(0, 1, l);
    c += float3(PF(2), PF(3), PF(4)) * (1.2 - t);
    if (PI(3) == 1) { float gm = PF(10); c = float3(PowZ(c.r, gm), PowZ(c.g, gm), PowZ(c.b, gm)); }
    float fx = (float)id.x / per, fy = (float)id.y / per, tv;
    if (tex == 1) { tv = 1 - abs(Fbm(fx * 1.7, fy * 1.7, ts0, 5) * 2 - 1); float t2 = tv * tv, t3 = t2 * tv; tv = t3 * t3 * 1.4 - 0.15; }
    else if (tex == 2) tv = HashT((int)id.x / speck, (int)id.y / speck, ts0) > 0.985 ? 1.0 : (Fbm(fx, fy, ts0, 3) - 0.5) * 0.5;
    else if (tex == 3) tv = (Fbm(fx * 0.25, fy * 4, ts0, 4) - 0.5) * 1.6;
    else if (tex == 4) tv = (Fbm(fx * 3, fy * 3, ts0, 5) - 0.5) * 1.2;
    else tv = (Fbm(fx * 0.6, fy * 0.6, ts0, 6) - 0.5) * 1.8;
    float a = tv * PF(5);
    c = float3(SoftLight(C01(c.r), C01(0.5 + a)), SoftLight(C01(c.g), C01(0.5 + a)), SoftLight(C01(c.b), C01(0.5 + a * 0.8)));
    float dx = (float)id.x - PF(8), dy = (float)id.y - PF(9);
    float d = sqrt(dx * dx + dy * dy) / PF(7);
    RGBOUT(c * (1.08 - 0.55 * SStep(0.15, 1.2, d)))
}

// ---------- Style.Retrolux. In3: the grade's 11 values (GradeApply reads it). In4: the leaks, six values each
// (x, y, size, r, g, b). PF: 0 strength, 1 gamma, 2 contrast, 3 saturation, 4 leaks, 5 half-diagonal, 6 width,
// 7 height. PI: 0 leak count, 1 use gamma ----------
[numthreads(16,16,1)] void Retrolux(uint3 id : SV_DispatchThreadID) {
    GUARD
    RGBIN
    c = GradeApply(c, PF(0));
    if (PI(1) == 1) { float gm = PF(1); c = float3(PowZ(c.r, gm), PowZ(c.g, gm), PowZ(c.b, gm)); }
    if (PF(2) != 0) c = float3(Contrast(c.r, PF(2)), Contrast(c.g, PF(2)), Contrast(c.b, PF(2)));
    c = Sat3(c, 1 + PF(3));
    if (PF(4) > 0)
        for (int k = 0; k < PI(0); k++) {
            float dx = (float)id.x - In4[k * 6], dy = (float)id.y - In4[k * 6 + 1], s = In4[k * 6 + 2];
            float a = exp(-(dx * dx + dy * dy) / (2 * s * s * 0.25)) * PF(4) * 0.85;
            c = float3(Screen(c.r, In4[k * 6 + 3] * a), Screen(c.g, In4[k * 6 + 4] * a), Screen(c.b, In4[k * 6 + 5] * a));
        }
    float ddx = (float)id.x - PF(6) * 0.5, ddy = (float)id.y - PF(7) * 0.5;
    float dd = sqrt(ddx * ddx + ddy * ddy) / PF(5);
    RGBOUT(c * (1 - 0.45 * PF(0) * SStep(0.55, 1.3, dd)))
}
";
    }
}
