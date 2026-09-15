namespace TestOptionStrategy.Server.Common
{
    public enum OptionType
    {
        Call,
        Put
    }

    public class OptionPriceAndGreeks
    {
        public double Price;
        public double Delta;
        public double Gamma;
        public double Theta;
        public double Vega;
        public double Rho;
    }

    public class GreeksCalculator
    {
        public OptionPriceAndGreeks GetPriceAndGreeks(
            OptionType optionType,
            double spotPrice,
            double strike,
            double daysToExpiry,
            double riskFreeRate,
            double sigma)
        {
            OptionPriceAndGreeks result = new OptionPriceAndGreeks();
            double time = daysToExpiry / 365.0;
            if (time < 0)
            {
                result.Price = double.NaN;
                return result;
            }
            if (time == 0)
            {
                result.Price = optionType == OptionType.Call ? Math.Max(spotPrice - strike, 0) : Math.Max(strike - spotPrice, 0);
                return result;
            }
            if (sigma <= 0 || double.IsNaN(sigma) || double.IsInfinity(sigma) || spotPrice <= 0 || strike <= 0)
            {
                if (optionType == OptionType.Call)
                {
                    result.Price = Math.Max(spotPrice - strike, 0);
                    result.Delta = spotPrice > strike ? 1 : 0;
                }
                else
                {
                    result.Price = Math.Max(strike - spotPrice, 0);
                    result.Delta = spotPrice < strike ? -1 : 0;
                }
                return result;
            }

            double sqrtT = Math.Sqrt(time);
            double d1 = (Math.Log(spotPrice / strike) + (riskFreeRate + sigma * sigma / 2.0) * time) / (sigma * sqrtT);
            double d2 = d1 - sigma * sqrtT;
            double pdf = Math.Exp(-d1 * d1 / 2.0) / Math.Sqrt(2.0 * Math.PI);
            double discount = Math.Exp(-riskFreeRate * time);

            if (optionType == OptionType.Call)
            {
                result.Price = spotPrice * NormalCdf(d1) - strike * discount * NormalCdf(d2);
                result.Delta = NormalCdf(d1);
                result.Theta = -spotPrice * sigma * pdf / (2.0 * sqrtT) - riskFreeRate * strike * discount * NormalCdf(d2);
                result.Rho = strike * time * discount * NormalCdf(d2);
            }
            else
            {
                result.Price = strike * discount * NormalCdf(-d2) - spotPrice * NormalCdf(-d1);
                result.Delta = NormalCdf(d1) - 1.0;
                result.Theta = -spotPrice * sigma * pdf / (2.0 * sqrtT) + riskFreeRate * strike * discount * NormalCdf(-d2);
                result.Rho = -strike * time * discount * NormalCdf(-d2);
            }

            result.Gamma = pdf / (spotPrice * sigma * sqrtT);
            result.Vega = spotPrice * sqrtT * pdf;

            return result;
        }

        public double CalculateGammaFromVega(double vega, double spotPrice, double sigma, double timeYears)
        {
            if (spotPrice <= 0 || sigma <= 0 || timeYears <= 0)
            {
                return 0;
            }
            return vega / (spotPrice * spotPrice * sigma * timeYears);
        }

        public double CalculateImpliedVolatility(
            double optionPrice,
            double spotPrice,
            double strike,
            double daysToExpiry,
            double riskFreeRate,
            OptionType optionType)
        {
            double time = daysToExpiry / 365.0;
            if (time <= 0)
            {
                return double.NaN;
            }
            double sigma = 0.5;
            double tolerance = 1e-8;
            for (int i = 0; i < 200; i++)
            {
                OptionPriceAndGreeks result = GetPriceAndGreeks(optionType, spotPrice, strike, daysToExpiry, riskFreeRate, sigma);
                double diff = result.Price - optionPrice;
                if (Math.Abs(diff) < tolerance)
                {
                    return sigma;
                }
                double vega = result.Vega;
                if (vega == 0 || double.IsNaN(vega))
                {
                    break;
                }
                sigma = sigma - diff / vega;
                if (sigma <= 0 || sigma > 10)
                {
                    return double.NaN;
                }
            }
            return double.NaN;
        }

        public double NormalCdf(double x)
        {
            return 0.5 * (1.0 + Erf(x / Math.Sqrt(2.0)));
        }

        private double Erf(double x)
        {
            double a1 = 0.254829592;
            double a2 = -0.284496736;
            double a3 = 1.421413741;
            double a4 = -1.453152027;
            double a5 = 1.061405429;
            double p = 0.3275911;

            int sign = x < 0 ? -1 : 1;
            double absX = Math.Abs(x);
            double t = 1.0 / (1.0 + p * absX);
            double y = ((((a5 * t + a4) * t + a3) * t + a2) * t + a1) * t;
            return sign * (1.0 - y * Math.Exp(-absX * absX));
        }
    }
}
