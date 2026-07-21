namespace Basil.Domain.Login;

public enum Country : byte
{
    Oc = 1, Eu = 2, Ad = 3, Ae = 4, Af = 5, Ag = 6, Ai = 7, Al = 8,
    Am = 9, An = 10, Ao = 11, Aq = 12, Ar = 13, As = 14, At = 15, Au = 16,
    Aw = 17, Az = 18, Ba = 19, Bb = 20, Bd = 21, Be = 22, Bf = 23, Bg = 24,
    Bh = 25, Bi = 26, Bj = 27, Bm = 28, Bn = 29, Bo = 30, Br = 31, Bs = 32,
    Bt = 33, Bv = 34, Bw = 35, By = 36, Bz = 37, Ca = 38, Cc = 39, Cd = 40,
    Cf = 41, Cg = 42, Ch = 43, Ci = 44, Ck = 45, Cl = 46, Cm = 47, Cn = 48,
    Co = 49, Cr = 50, Cu = 51, Cv = 52, Cx = 53, Cy = 54, Cz = 55, De = 56,
    Dj = 57, Dk = 58, Dm = 59, Do = 60, Dz = 61, Ec = 62, Ee = 63, Eg = 64,
    Eh = 65, Er = 66, Es = 67, Et = 68, Fi = 69, Fj = 70, Fk = 71, Fm = 72,
    Fo = 73, Fr = 74, Fx = 75, Ga = 76, Gb = 77, Gd = 78, Ge = 79, Gf = 80,
    Gh = 81, Gi = 82, Gl = 83, Gm = 84, Gn = 85, Gp = 86, Gq = 87, Gr = 88,
    Gs = 89, Gt = 90, Gu = 91, Gw = 92, Gy = 93, Hk = 94, Hm = 95, Hn = 96,
    Hr = 97, Ht = 98, Hu = 99, Id = 100, Ie = 101, Il = 102, In = 103, Io = 104,
    Iq = 105, Ir = 106, Is = 107, It = 108, Jm = 109, Jo = 110, Jp = 111, Ke = 112,
    Kg = 113, Kh = 114, Ki = 115, Km = 116, Kn = 117, Kp = 118, Kr = 119, Kw = 120,
    Ky = 121, Kz = 122, La = 123, Lb = 124, Lc = 125, Li = 126, Lk = 127, Lr = 128,
    Ls = 129, Lt = 130, Lu = 131, Lv = 132, Ly = 133, Ma = 134, Mc = 135, Md = 136,
    Mg = 137, Mh = 138, Mk = 139, Ml = 140, Mm = 141, Mn = 142, Mo = 143, Mp = 144,
    Mq = 145, Mr = 146, Ms = 147, Mt = 148, Mu = 149, Mv = 150, Mw = 151, Mx = 152,
    My = 153, Mz = 154, Na = 155, Nc = 156, Ne = 157, Nf = 158, Ng = 159, Ni = 160,
    Nl = 161, No = 162, Np = 163, Nr = 164, Nu = 165, Nz = 166, Om = 167, Pa = 168,
    Pe = 169, Pf = 170, Pg = 171, Ph = 172, Pk = 173, Pl = 174, Pm = 175, Pn = 176,
    Pr = 177, Ps = 178, Pt = 179, Pw = 180, Py = 181, Qa = 182, Re = 183, Ro = 184,
    Ru = 185, Rw = 186, Sa = 187, Sb = 188, Sc = 189, Sd = 190, Se = 191, Sg = 192,
    Sh = 193, Si = 194, Sj = 195, Sk = 196, Sl = 197, Sm = 198, Sn = 199, So = 200,
    Sr = 201, St = 202, Sv = 203, Sy = 204, Sz = 205, Tc = 206, Td = 207, Tf = 208,
    Tg = 209, Th = 210, Tj = 211, Tk = 212, Tm = 213, Tn = 214, To = 215, Tl = 216,
    Tr = 217, Tt = 218, Tv = 219, Tw = 220, Tz = 221, Ua = 222, Ug = 223, Um = 224,
    Us = 225, Uy = 226, Uz = 227, Va = 228, Vc = 229, Ve = 230, Vg = 231, Vi = 232,
    Vn = 233, Vu = 234, Wf = 235, Ws = 236, Ye = 237, Yt = 238, Rs = 239, Za = 240,
    Zm = 241, Me = 242, Zw = 243, Xx = 244, A2 = 245, O1 = 246, Ax = 247, Gg = 248,
    Im = 249, Je = 250, Bl = 251, Mf = 252
}

public static class CountryExtensions
{
    public static string ToAcronym(this Country code)
    {
        return code.ToString().ToLowerInvariant();
    }
}