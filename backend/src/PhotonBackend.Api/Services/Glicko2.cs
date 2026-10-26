namespace PhotonBackend.Api.Services;

/// <summary>Satu rating Glicko-2. Rating &amp; Rd pada skala 1500 yang familiar; Volatility tak berdimensi.</summary>
public readonly record struct Glicko2Rating(double Rating, double Rd, double Volatility);

/// <summary>Satu hasil game dalam periode rating: rating/RD lawan + skor (1 menang, 0.5 seri, 0 kalah).</summary>
public readonly record struct Glicko2Game(double OpponentRating, double OpponentRd, double Score);

/// <summary>
/// Implementasi Glicko-2 (Mark Glickman). Dipakai untuk ladder Solo 1v1 (doc 24).
/// Nilai default mengikuti paper: rating 1500, RD 350, volatility 0.06, konstanta sistem tau 0.5.
/// Satu match 1v1 = periode rating berisi 1 game (live update), dipanggil simetris untuk kedua pemain.
/// </summary>
public static class Glicko2
{
    public const double DefaultRating = 1500.0;
    public const double DefaultRd = 350.0;
    public const double DefaultVolatility = 0.06;
    private const double Scale = 173.7178; // faktor konversi ke skala internal Glicko-2

    /// <summary>
    /// Perbarui rating satu pemain dari game-game pada satu periode.
    /// Tanpa game → hanya RD yang membesar (keyakinan meluruh), dibatasi <see cref="DefaultRd"/>.
    /// </summary>
    public static Glicko2Rating Update(Glicko2Rating player, IReadOnlyList<Glicko2Game> games, double tau = 0.5)
    {
        double mu = (player.Rating - DefaultRating) / Scale;
        double phi = player.Rd / Scale;
        double sigma = player.Volatility;

        if (games.Count == 0)
        {
            double phiStarOnly = Math.Sqrt(phi * phi + sigma * sigma);
            return new Glicko2Rating(player.Rating, Math.Min(phiStarOnly * Scale, DefaultRd), sigma);
        }

        // Langkah 3 & 4: estimasi varians (v) dan arah perubahan (deltaSum).
        double vInv = 0.0;
        double deltaSum = 0.0;
        foreach (var game in games)
        {
            double muj = (game.OpponentRating - DefaultRating) / Scale;
            double phij = game.OpponentRd / Scale;
            double g = G(phij);
            double e = E(mu, muj, g);
            vInv += g * g * e * (1.0 - e);
            deltaSum += g * (game.Score - e);
        }
        double v = 1.0 / vInv;
        double delta = v * deltaSum;

        // Langkah 5: volatilitas baru via algoritma Illinois (regula falsi).
        double a = Math.Log(sigma * sigma);
        double tau2 = tau * tau;
        double phi2 = phi * phi;

        double F(double x)
        {
            double ex = Math.Exp(x);
            double num = ex * (delta * delta - phi2 - v - ex);
            double den = 2.0 * (phi2 + v + ex) * (phi2 + v + ex);
            return num / den - (x - a) / tau2;
        }

        double aa = a;
        double bb;
        if (delta * delta > phi2 + v)
        {
            bb = Math.Log(delta * delta - phi2 - v);
        }
        else
        {
            double k = 1.0;
            while (F(a - k * tau) < 0.0) k += 1.0;
            bb = a - k * tau;
        }

        double fa = F(aa);
        double fb = F(bb);
        const double eps = 1e-6;
        while (Math.Abs(bb - aa) > eps)
        {
            double c = aa + (aa - bb) * fa / (fb - fa);
            double fc = F(c);
            if (fc * fb <= 0.0) { aa = bb; fa = fb; }
            else { fa /= 2.0; }
            bb = c; fb = fc;
        }
        double sigmaPrime = Math.Exp(aa / 2.0);

        // Langkah 6 & 7: RD & rating baru.
        double phiStar = Math.Sqrt(phi2 + sigmaPrime * sigmaPrime);
        double phiPrime = 1.0 / Math.Sqrt(1.0 / (phiStar * phiStar) + 1.0 / v);
        double muPrime = mu + phiPrime * phiPrime * deltaSum;

        // Langkah 8: kembali ke skala 1500.
        return new Glicko2Rating(Scale * muPrime + DefaultRating, Scale * phiPrime, sigmaPrime);
    }

    private static double G(double phi) => 1.0 / Math.Sqrt(1.0 + 3.0 * phi * phi / (Math.PI * Math.PI));
    private static double E(double mu, double muj, double g) => 1.0 / (1.0 + Math.Exp(-g * (mu - muj)));
}
