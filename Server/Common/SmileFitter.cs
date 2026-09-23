namespace TestOptionStrategy.Server.Common
{
    public static class SmileFitter
    {
        public static double[] FitQuadratic(List<(double X, double Y, double Weight)> points)
        {
            if (points.Count == 0)
            {
                return new double[] { 0, 0, 0 };
            }
            if (points.Count == 1)
            {
                return new double[] { points[0].Y, 0, 0 };
            }
            if (points.Count == 2)
            {
                return FitLinear(points);
            }

            double s0 = 0, s1 = 0, s2 = 0, s3 = 0, s4 = 0, t0 = 0, t1 = 0, t2 = 0;
            foreach ((double X, double Y, double Weight) p in points)
            {
                double x = p.X;
                double y = p.Y;
                double w = p.Weight;
                double x2 = x * x;
                double x3 = x2 * x;
                double x4 = x3 * x;
                s0 += w;
                s1 += w * x;
                s2 += w * x2;
                s3 += w * x3;
                s4 += w * x4;
                t0 += w * y;
                t1 += w * x * y;
                t2 += w * x2 * y;
            }

            double[][] matrix =
            {
                new double[] { s0, s1, s2 },
                new double[] { s1, s2, s3 },
                new double[] { s2, s3, s4 }
            };
            double[] rhs = { t0, t1, t2 };

            double[]? solution = Solve3(matrix, rhs);
            return solution ?? FitLinear(points);
        }

        public static double EvaluateQuadratic(double[] coeff, double x)
        {
            return coeff[0] + coeff[1] * x + coeff[2] * x * x;
        }

        private static double[] FitLinear(List<(double X, double Y, double Weight)> points)
        {
            double s0 = 0, s1 = 0, s2 = 0, t0 = 0, t1 = 0;
            foreach ((double X, double Y, double Weight) p in points)
            {
                s0 += p.Weight;
                s1 += p.Weight * p.X;
                s2 += p.Weight * p.X * p.X;
                t0 += p.Weight * p.Y;
                t1 += p.Weight * p.X * p.Y;
            }
            double det = s0 * s2 - s1 * s1;
            if (Math.Abs(det) < 1e-12)
            {
                double meanY = s0 > 0 ? t0 / s0 : 0;
                return new double[] { meanY, 0, 0 };
            }
            double a = (t0 * s2 - t1 * s1) / det;
            double b = (s0 * t1 - s1 * t0) / det;
            return new double[] { a, b, 0 };
        }

        private static double[]? Solve3(double[][] matrix, double[] rhs)
        {
            double[,] a = new double[3, 4];
            for (int i = 0; i < 3; i++)
            {
                for (int j = 0; j < 3; j++)
                {
                    a[i, j] = matrix[i][j];
                }
                a[i, 3] = rhs[i];
            }

            for (int col = 0; col < 3; col++)
            {
                int pivot = col;
                for (int r = col + 1; r < 3; r++)
                {
                    if (Math.Abs(a[r, col]) > Math.Abs(a[pivot, col]))
                    {
                        pivot = r;
                    }
                }
                if (Math.Abs(a[pivot, col]) < 1e-12)
                {
                    return null;
                }
                if (pivot != col)
                {
                    for (int j = 0; j < 4; j++)
                    {
                        double tmp = a[pivot, j];
                        a[pivot, j] = a[col, j];
                        a[col, j] = tmp;
                    }
                }
                double divisor = a[col, col];
                for (int j = col; j < 4; j++)
                {
                    a[col, j] /= divisor;
                }
                for (int r = 0; r < 3; r++)
                {
                    if (r == col)
                    {
                        continue;
                    }
                    double factor = a[r, col];
                    for (int j = col; j < 4; j++)
                    {
                        a[r, j] -= factor * a[col, j];
                    }
                }
            }
            return new double[] { a[0, 3], a[1, 3], a[2, 3] };
        }
    }
}
