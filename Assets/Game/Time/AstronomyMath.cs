using System;
using UnityEngine;

namespace Fengling.Time
{
    /// <summary>
    /// 低精度天文计算：日期 -> 太阳/月亮方向、月相。
    /// 参考 Meeus《Astronomical Algorithms》低精度章节与 SunCalcNet 的月亮黄经思路。
    /// 不追求星历级精度，但保证日月相位、方向和月相一致。
    /// </summary>
    public static class AstronomyMath
    {
        public const double SynodicMonth = 29.53059;
        private const double Deg2Rad = Math.PI / 180.0;
        private const double Rad2Deg = 180.0 / Math.PI;
        private const double Obliquity = 23.4397 * Deg2Rad;

        public static double JulianDay(DateTime utc)
        {
            int y = utc.Year;
            int m = utc.Month;
            double day = utc.Day + (utc.Hour + (utc.Minute + utc.Second / 60.0) / 60.0) / 24.0;
            if (m <= 2) { y -= 1; m += 12; }
            int a = y / 100;
            int b = 2 - a + a / 4;
            return Math.Floor(365.25 * (y + 4716)) + Math.Floor(30.6001 * (m + 1)) + day + b - 1524.5;
        }

        public static double DaysSinceJ2000(DateTime utc) => JulianDay(utc) - 2451545.0;

        public static double SunEclipticLongitude(double d)
        {
            double g = Normalize360(357.529 + 0.98560028 * d);
            double q = Normalize360(280.459 + 0.98564736 * d);
            return Normalize360(q + 1.915 * Math.Sin(g * Deg2Rad) + 0.020 * Math.Sin(2.0 * g * Deg2Rad));
        }

        public static double MoonEclipticLongitude(double d)
        {
            double t = d / 36525.0;
            double lp = Normalize360(218.316 + 481267.881 * t);
            double m = Normalize360(134.963 + 477198.867 * t);
            double f = Normalize360(93.272 + 483202.017 * t);
            double dd = Normalize360(297.850 + 445267.111 * t);
            double ms = Normalize360(357.529 + 35999.050 * t);

            double lon = lp
                + 6.289 * Sin(m)
                + 1.274 * Sin(2 * dd - m)
                + 0.658 * Sin(2 * dd)
                + 0.214 * Sin(2 * m)
                - 0.186 * Sin(ms)
                - 0.114 * Sin(2 * f)
                + 0.059 * Sin(2 * dd - 2 * m)
                + 0.057 * Sin(2 * dd - ms)
                + 0.053 * Sin(2 * dd + m)
                + 0.046 * Sin(2 * dd - 2 * f)
                - 0.041 * Sin(m - ms)
                - 0.035 * Sin(dd)
                - 0.031 * Sin(m + ms)
                - 0.015 * Sin(2 * f - 2 * dd)
                + 0.011 * Sin(m - 4 * dd);
            return Normalize360(lon);
        }

        public static double MoonEclipticLatitude(double d)
        {
            double t = d / 36525.0;
            double m = Normalize360(134.963 + 477198.867 * t);
            double f = Normalize360(93.272 + 483202.017 * t);
            double dd = Normalize360(297.850 + 445267.111 * t);
            return 5.128 * Sin(f)
                + 0.281 * Sin(m + f)
                + 0.278 * Sin(f - m)
                + 0.173 * Sin(2 * dd - f)
                + 0.055 * Sin(2 * dd + f - m)
                + 0.046 * Sin(2 * dd - f - m)
                + 0.033 * Sin(2 * dd + f)
                + 0.017 * Sin(2 * m + f);
        }

        public static double MoonAgeDays(double sunLon, double moonLon)
        {
            return Normalize360(moonLon - sunLon) / 360.0 * SynodicMonth;
        }

        public static double MoonIlluminatedFraction(double elongationRad)
        {
            return (1.0 - Math.Cos(elongationRad)) * 0.5;
        }

        public static Vector3 HorizonDirection(double eclipticLonDeg, double eclipticLatDeg,
                                               double d, double latitudeDeg, double longitudeDeg = 0.0)
        {
            double lon = eclipticLonDeg * Deg2Rad;
            double lat = eclipticLatDeg * Deg2Rad;
            double sinDec = Math.Sin(lat) * Math.Cos(Obliquity) +
                            Math.Cos(lat) * Math.Sin(Obliquity) * Math.Sin(lon);
            double dec = Math.Asin(Math.Clamp(sinDec, -1.0, 1.0));
            double ra = Math.Atan2(Math.Sin(lon) * Math.Cos(Obliquity) - Math.Tan(lat) * Math.Sin(Obliquity),
                                   Math.Cos(lon));
            ra = Normalize360(ra * Rad2Deg);

            double gmst = Normalize360(280.46061837 + 360.98564736629 * d + longitudeDeg);
            double hourAngle = Normalize360(gmst - ra);
            double ha = hourAngle * Deg2Rad;
            double phi = latitudeDeg * Deg2Rad;

            double sinAlt = Math.Sin(phi) * Math.Sin(dec) + Math.Cos(phi) * Math.Cos(dec) * Math.Cos(ha);
            sinAlt = Math.Clamp(sinAlt, -1.0, 1.0);
            double alt = Math.Asin(sinAlt);
            double az = Math.Atan2(Math.Sin(ha), Math.Cos(ha) * Math.Sin(phi) - Math.Tan(dec) * Math.Cos(phi)) + Math.PI;
            az = Normalize360(az * Rad2Deg) * Deg2Rad;

            return new Vector3(
                (float)(Math.Cos(alt) * Math.Sin(az)),
                (float)Math.Sin(alt),
                (float)(Math.Cos(alt) * Math.Cos(az))).normalized;
        }

        private static double Sin(double degrees) => Math.Sin(degrees * Deg2Rad);
        private static double Normalize360(double value)
        {
            value %= 360.0;
            return value < 0.0 ? value + 360.0 : value;
        }
    }
}
