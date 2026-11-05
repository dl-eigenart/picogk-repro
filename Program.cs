using System.Globalization;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Numerics;
using PicoGK;
using Leap71.ShapeKernel;

namespace PicoGKRepro
{
    /// <summary>
    /// Drei eigenstaendige Reproduktionen, je ein Aufruf:
    ///   dotnet run -- traegheit <voxel_mm>   Befund 1: Measure.matGetMomentOfInertia gegen 2/5 m R^2
    ///   dotnet run -- innen     <voxel_mm>   Befund 2: Voxels.bIsInside und ScalarField.bGetValue an einer Kugel
    ///   dotnet run -- volumen   <voxel_mm>   Befund 3: Measure.fGetVolume an einer geschlossenen Hohlkugel
    ///   dotnet run -- regress   <voxel_mm>   Regressionstest bIsInside nach herbstritt (PR 115), Exitcode 0 = bestanden
    /// Alle Zahlen gegen geschlossene Formeln. Keine Abhaengigkeit ausser PicoGK und ShapeKernel.
    /// </summary>
    public static class App
    {
        static readonly CultureInfo CI = CultureInfo.InvariantCulture;
        static int nExit = 0;

        public static int Main(string[] a)
        {
            if (a.Length < 2) { Console.WriteLine("Aufruf: traegheit|innen|innen2|volumen|regress <voxel_mm>"); return 1; }
            float fVoxel = float.Parse(a[1], CI);
            Action fn = a[0] switch
            {
                "traegheit" => () => Traegheit(fVoxel),
                "innen"     => () => Innen(fVoxel),
                "innen2"    => () => Innen2(fVoxel),
                "volumen"   => () => Volumen(fVoxel),
                "regress"   => () => Regress(),
                _ => throw new ArgumentException(a[0])
            };
            Library.Go(fVoxel, () => { try { fn(); } catch (Exception e) { Console.WriteLine("FEHLER: " + e); } },
                       strLogFilePath: $"/tmp/picogk_repro_{a[0]}_{a[1]}.log",
                       bEndAppWithTask: true);
            return nExit;
        }

        static string F(double d) => d.ToString("0.000000E+00", CI);
        static string P(double dIst, double dSoll) => ((dIst / dSoll - 1.0) * 100.0).ToString("+0.00;-0.00", CI) + " %";

        // ------------------------------------------------------------------
        // Befund 1: Traegheit
        // ------------------------------------------------------------------
        static void Traegheit(float fVoxel)
        {
            float fR = 50f; float fDichte = 1000f; // mm, kg/m3
            Console.WriteLine($"# Befund 1: Traegheit, Kugel R = {fR} mm, Voxel = {fVoxel} mm, Dichte = {fDichte} kg/m3");
            Voxels vox = new BaseSphere(new LocalFrame(Vector3.Zero), fR).voxConstruct();

            double dVolSoll = 4.0 / 3.0 * Math.PI * Math.Pow(fR, 3);            // mm3
            double dMasse   = fDichte * dVolSoll * 1e-9;                          // kg
            double dISoll   = 0.4 * dMasse * fR * fR;                             // kg mm2 (Measure rechnet Positionen in mm), gilt fuer alle drei Achsen

            LocalFrame oRef = new(Vector3.Zero);
            double[,] mat = Measure.matGetMomentOfInertia(vox, oRef, fDichte);
            Console.WriteLine($"Soll (2/5 m R^2):                {F(dISoll)} kg mm2");
            Console.WriteLine($"Measure.matGetMomentOfInertia:   Ixx {F(mat[0,0])}  Iyy {F(mat[1,1])}  Izz {F(mat[2,2])}   Abw. {P(mat[0,0], dISoll)} / {P(mat[1,1], dISoll)} / {P(mat[2,2], dISoll)}");

            // Dieselbe Rasterung wie in Measure, aber ohne den Rueckgabewert von bGetValue als Filter:
            // nur das Vorzeichen des Abstandswerts entscheidet (so wie in Discussion #71 empfohlen).
            ScalarField oSD = new(vox);
            vox.CalculateProperties(out float fVol, out BBox3 oBox);
            oBox.Grow(1f);
            Vector3 vecSize = oBox.vecSize();
            long nAktiv = 0, nInnenAlle = 0;
            double dIxx = 0, dIyy = 0, dIzz = 0;
            double dVoxVol = (double)fVoxel * fVoxel * fVoxel;
            for (float x = 0; x < vecSize.X; x += fVoxel)
            for (float y = 0; y < vecSize.Y; y += fVoxel)
            for (float z = 0; z < vecSize.Z; z += fVoxel)
            {
                Vector3 p = oBox.vecMin + new Vector3(x, y, z);
                bool bOn = oSD.bGetValue(p, out float fSD);
                if (fSD <= 0f)
                {
                    nInnenAlle++;
                    if (bOn) nAktiv++;
                    double dm = fDichte * dVoxVol * 1e-9;
                    double px = p.X, py = p.Y, pz = p.Z;   // mm, wie in Measure
                    dIxx += dm * (py * py + pz * pz);
                    dIyy += dm * (px * px + pz * pz);
                    dIzz += dm * (px * px + py * py);
                }
            }
            Console.WriteLine($"Rasterpunkte innen (fSD<=0): {nInnenAlle}, davon mit gespeichertem Wert (bGetValue==true): {nAktiv}  ({100.0*nAktiv/Math.Max(1,nInnenAlle):0.0} %)");
            Console.WriteLine($"Raster ohne Rueckgabewert-Filter: Ixx {F(dIxx)}  Iyy {F(dIyy)}  Izz {F(dIzz)}   Abw. {P(dIxx, dISoll)} / {P(dIyy, dISoll)} / {P(dIzz, dISoll)}");
            Console.WriteLine($"Volumen PicoGK: {F(fVol)} mm3, Soll {F(dVolSoll)} mm3, Abw. {P(fVol, dVolSoll)}");
        }

        // ------------------------------------------------------------------
        // Befund 2: bIsInside
        // ------------------------------------------------------------------
        static void Innen(float fVoxel)
        {
            float fR = 50f;
            Console.WriteLine($"# Befund 2: bIsInside, Kugel R = {fR} mm, Voxel = {fVoxel} mm, Schmalband = 3 Voxel = {3*fVoxel} mm");
            Voxels vox = new BaseSphere(new LocalFrame(Vector3.Zero), fR).voxConstruct();
            ScalarField oSD = new(vox);
            Console.WriteLine("x[mm]   erwartet   bIsInside   bGetValue.on   Wert[mm]");
            foreach (float x in new[] { 0f, 25f, 45f, 48f, 49.5f, 50.5f, 52f, 55f, 60f, 75f, 100f, 200f })
            {
                Vector3 p = new(x, 0f, 0f);
                bool bIn = vox.bIsInside(p);
                bool bOn = oSD.bGetValue(p, out float fSD);
                string sErw = x < fR ? "innen" : "aussen";
                string sMark = (bIn == (x < fR)) ? "" : "   <-- FALSCH";
                Console.WriteLine($"{x,6:0.0}   {sErw,-8}   {bIn,-9}   {bOn,-12}   {fSD,9:0.000}{sMark}");
            }
            // Gegenprobe nach einer Booleschen Operation: Kugel minus kleinere Kugel
            Voxels voxSchale = new BaseSphere(new LocalFrame(Vector3.Zero), fR).voxConstruct();
            voxSchale.BoolSubtract(new BaseSphere(new LocalFrame(Vector3.Zero), 25f).voxConstruct());
            ScalarField oSD2 = new(voxSchale);
            Console.WriteLine("Nach BoolSubtract (Schale 25..50 mm):");
            Console.WriteLine("x[mm]   erwartet   bIsInside   bGetValue.on   Wert[mm]");
            foreach (float x in new[] { 0f, 10f, 24f, 26f, 37.5f, 49f, 51f, 75f })
            {
                Vector3 p = new(x, 0f, 0f);
                bool bIn = voxSchale.bIsInside(p);
                bool bOn = oSD2.bGetValue(p, out float fSD);
                bool bErw = x > 25f && x < fR;
                string sMark = (bIn == bErw) ? "" : "   <-- FALSCH";
                Console.WriteLine($"{x,6:0.0}   {(bErw ? "innen" : "aussen"),-8}   {bIn,-9}   {bOn,-12}   {fSD,9:0.000}{sMark}");
            }
        }

        // ------------------------------------------------------------------
        // Befund 2, Ursachenprobe: dieselbe native Funktion mit anderer Rueckgabe-Marshallierung.
        // PicoGK deklariert bool-Rueckgaben ohne MarshalAs, also als 4-Byte-BOOL; C++ liefert 1 Byte.
        // ------------------------------------------------------------------
        [DllImport("picogk.26.2", CallingConvention = CallingConvention.Cdecl, EntryPoint = "Voxels_bIsInside")]
        [return: MarshalAs(UnmanagedType.I1)]
        static extern bool bIsInside_I1(long hLib, long hVox, in Vector3 vec);

        [DllImport("picogk.26.2", CallingConvention = CallingConvention.Cdecl, EntryPoint = "Voxels_bIsInside")]
        static extern int bIsInside_RohEAX(long hLib, long hVox, in Vector3 vec);

        [DllImport("picogk.26.2", CallingConvention = CallingConvention.Cdecl, EntryPoint = "ScalarField_bGetValue")]
        static extern int bGetValue_RohEAX(long hLib, IntPtr hField, in Vector3 vec, ref float fValue);

        static long lHandle(object o)
        {
            FieldInfo f = o.GetType().GetField("hThis", BindingFlags.NonPublic | BindingFlags.Instance)
                          ?? throw new Exception("hThis nicht gefunden");
            object h = f.GetValue(o)!;
            object v = h.GetType().GetProperty("Value")!.GetValue(h)!;
            return v is IntPtr ip ? ip.ToInt64() : (long)v;
        }

        static void Innen2(float fVoxel)
        {
            float fR = 50f;
            Console.WriteLine($"# Befund 2, Ursachenprobe: Kugel R = {fR} mm, Voxel = {fVoxel} mm");
            Voxels vox = new BaseSphere(new LocalFrame(Vector3.Zero), fR).voxConstruct();
            ScalarField oSD = new(vox);
            long hLib = lHandle(Library.oLibrary());
            long hVox = lHandle(vox);
            IntPtr hSD = new(lHandle(oSD));
            Console.WriteLine("x[mm]   erwartet   PicoGK.bIsInside   MarshalAs(I1)   roher EAX (hex)   | bGetValue roher EAX (hex)");
            foreach (float x in new[] { 0f, 25f, 45f, 49.5f, 50.5f, 55f, 75f, 100f, 200f })
            {
                Vector3 p = new(x, 0f, 0f);
                bool bAlt = vox.bIsInside(p);
                bool bI1  = bIsInside_I1(hLib, hVox, p);
                int  nRoh = bIsInside_RohEAX(hLib, hVox, p);
                float fV = 0f;
                int  nRohGV = bGetValue_RohEAX(hLib, hSD, p, ref fV);
                string sMark = (bI1 == (x < fR)) ? "" : "   <-- auch I1 falsch";
                Console.WriteLine($"{x,6:0.0}   {(x < fR ? "innen" : "aussen"),-8}   {bAlt,-16}   {bI1,-13}   0x{nRoh:X8}         | 0x{nRohGV:X8}{sMark}");
            }
        }

        // ------------------------------------------------------------------
        // Regressionstest bIsInside, nach dem Test von @herbstritt in
        // https://github.com/leap71/PicoGK/pull/115#issuecomment-6641886357
        // Kugel r = 10 mm im Ursprung, nur PicoGK, kein ShapeKernel.
        // Ohne den Fix ist der Punkt ausserhalb true. Exitcode 1 bei Fehler.
        // ------------------------------------------------------------------
        static void Regress()
        {
            Lattice lat = new();
            lat.AddSphere(Vector3.Zero, 10f);
            Voxels vox = new(lat);
            (Vector3 p, bool bErw)[] aProbe =
            {
                (Vector3.Zero,             true),
                (new Vector3(20, 0, 0),    false),
                (new Vector3(5, 0, 0),     true),
                (new Vector3(0, 15, 0),    false),
                (new Vector3(0, 0, -12),   false),
            };
            int nFalsch = 0;
            foreach (var (p, bErw) in aProbe)
            {
                bool bIst = vox.bIsInside(p);
                if (bIst != bErw) nFalsch++;
                Console.WriteLine($"bIsInside({p.X}, {p.Y}, {p.Z}) = {bIst,-5}  erwartet {bErw,-5}{(bIst == bErw ? "" : "   <-- FALSCH")}");
            }
            Console.WriteLine(nFalsch == 0 ? "PASS" : $"FAIL ({nFalsch} von {aProbe.Length} falsch)");
            nExit = nFalsch == 0 ? 0 : 1;
        }

        // ------------------------------------------------------------------
        // Befund 3: Volumen einer geschlossenen Schale
        // ------------------------------------------------------------------
        static void Volumen(float fVoxel)
        {
            float fRa = 100f, fRi = 50f, fBohrung = 10f;
            Console.WriteLine($"# Befund 3: Volumen, Hohlkugel Ra = {fRa} mm, Ri = {fRi} mm, Voxel = {fVoxel} mm");
            double dVa = 4.0 / 3.0 * Math.PI * Math.Pow(fRa, 3);
            double dVi = 4.0 / 3.0 * Math.PI * Math.Pow(fRi, 3);
            double dSchale = dVa - dVi;

            Voxels voxVoll = new BaseSphere(new LocalFrame(Vector3.Zero), fRa).voxConstruct();
            Voxels voxSchale = new BaseSphere(new LocalFrame(Vector3.Zero), fRa).voxConstruct();
            voxSchale.BoolSubtract(new BaseSphere(new LocalFrame(Vector3.Zero), fRi).voxConstruct());

            // dieselbe Schale mit einer Bohrung nach aussen (Hohlraum nicht mehr eingeschlossen)
            Voxels voxOffen = new Voxels(voxSchale);
            voxOffen.BoolSubtract(new BaseCylinder(new LocalFrame(new Vector3(0, 0, -150f)), 300f, fBohrung).voxConstruct());
            // Bohrungsvolumen innerhalb der Schale: Zylinder r=10 durch beide Wandbereiche, Naeherung ueber Netzvolumen unten

            Report("Vollkugel Ra",           voxVoll,   dVa);
            Report("Hohlkugel (geschlossen)", voxSchale, dSchale);
            Report("Hohlkugel mit Bohrung",   voxOffen,  double.NaN);
        }

        static void Report(string s, Voxels vox, double dSoll)
        {
            float fVol = Measure.fGetVolume(vox);
            Mesh msh = vox.mshAsMesh();
            double dNetz = dNetzvolumen(msh);
            string sSoll = double.IsNaN(dSoll) ? "   (kein geschlossener Sollwert)" : $"   Soll {F(dSoll)}   Abw. PicoGK {P(fVol, dSoll)}, Netz {P(dNetz, dSoll)}";
            Console.WriteLine($"{s,-26} PicoGK fGetVolume {F(fVol)} mm3   Netz (Tetraeder) {F(dNetz)} mm3   Dreiecke {msh.nTriangleCount()}{sSoll}");
        }

        /// Vorzeichenbehaftete Tetraedersumme ueber das Dreiecksnetz, unabhaengige Gegenprobe.
        static double dNetzvolumen(Mesh msh)
        {
            double d = 0;
            int n = msh.nTriangleCount();
            for (int i = 0; i < n; i++)
            {
                msh.GetTriangle(i, out Vector3 a, out Vector3 b, out Vector3 c);
                d += Vector3.Dot(a, Vector3.Cross(b, c)) / 6.0;
            }
            return Math.Abs(d);
        }
    }
}
