using System;

namespace ContractDateReplacer.Core.Services;

/// <summary>
/// Thuật toán chuyển đổi Âm lịch - Dương lịch Việt Nam chuẩn thiên văn học của Hồ Ngọc Đức (múi giờ GMT+7).
/// </summary>
public static class VietnameseLunarCalendar
{
    private const double TimeZone = 7.0;

    /// <summary>
    /// Chuyển đổi ngày dương lịch (ngày, tháng, năm) thành ngày Julian (Julian Day Number).
    /// </summary>
    public static int SolarToJulianDay(int day, int month, int year)
    {
        int a = (14 - month) / 12;
        int y = year + 4800 - a;
        int m = month + 12 * a - 3;
        int jd = day + (153 * m + 2) / 5 + 365 * y + y / 4 - y / 100 + y / 400 - 32045;
        if (jd < 2299161)
        {
            jd = day + (153 * m + 2) / 5 + 365 * y + y / 4 - 32083;
        }
        return jd;
    }

    /// <summary>
    /// Chuyển đổi số ngày Julian thành ngày dương lịch (DateTime).
    /// </summary>
    public static DateTime JulianDayToSolar(int jd)
    {
        int a, b, c;
        if (jd > 2299160)
        {
            a = jd + 32044;
            b = (4 * a + 3) / 146097;
            c = a - (146097 * b) / 4;
        }
        else
        {
            b = 0;
            c = jd + 32082;
        }

        int d = (4 * c + 3) / 1461;
        int e = c - (1461 * d) / 4;
        int m = (5 * e + 2) / 153;
        int day = e - (153 * m + 2) / 5 + 1;
        int month = m + 3 - 12 * (m / 10);
        int year = b * 100 + d - 4800 + (m / 10);
        return new DateTime(year, month, day);
    }

    private static int GetSunLongitude(int dayNumber, double timeZone = TimeZone)
    {
        double t = (dayNumber - 2415021.076998695 + 0.5 - timeZone / 24.0) / 36525.0;
        double t2 = t * t;
        double dr = Math.PI / 180.0;
        double l0 = 279.69668 + 36000.76892 * t + 0.0003025 * t2;
        double m = 358.47583 + 35999.04975 * t - 0.000150 * t2 - 0.0000033 * t2 * t;
        double c = (1.919460 - 0.004789 * t - 0.000014 * t2) * Math.Sin(m * dr)
                 + (0.020094 - 0.000100 * t) * Math.Sin(2 * m * dr)
                 + 0.000293 * Math.Sin(3 * m * dr);
        double theta = (l0 + c) * dr;
        theta -= Math.PI * 2.0 * Math.Floor(theta / (Math.PI * 2.0));
        return (int)Math.Floor(theta / (Math.PI / 6.0));
    }

    private static int GetNewMoonDay(int k, double timeZone = TimeZone)
    {
        double t = k / 1236.85;
        double t2 = t * t;
        double t3 = t2 * t;
        double dr = Math.PI / 180.0;
        double jd1 = 2415020.75933 + 29.53058868 * k + 0.0001178 * t2 - 0.0000002 * t3;

        double m = 359.2242 + 29.10535608 * k - 0.0000333 * t2 - 0.00000347 * t3;
        double mpr = 306.0253 + 385.81691806 * k + 0.0107306 * t2 + 0.00001236 * t3;
        double f = 21.2964 + 390.67050646 * k - 0.0016528 * t2 - 0.00000239 * t3;

        double c1 = (0.1734 - 0.000393 * t) * Math.Sin(m * dr)
                  + 0.0021 * Math.Sin(2 * dr * m)
                  - 0.4068 * Math.Sin(mpr * dr)
                  + 0.0161 * Math.Sin(2 * dr * mpr)
                  - 0.0004 * Math.Sin(3 * dr * mpr)
                  + 0.0104 * Math.Sin(2 * dr * f)
                  - 0.0051 * Math.Sin(dr * (m + mpr))
                  - 0.0074 * Math.Sin(dr * (m - mpr))
                  + 0.0004 * Math.Sin(dr * (2 * f + m))
                  - 0.0004 * Math.Sin(dr * (2 * f - m))
                  - 0.0006 * Math.Sin(dr * (2 * f + mpr))
                  + 0.0010 * Math.Sin(dr * (2 * f - mpr))
                  + 0.0005 * Math.Sin(dr * (m + 2 * mpr));

        double deltat;
        if (t < -11)
        {
            deltat = 0.001 + 0.000839 * t + 0.0002261 * t2 - 0.00000845 * t3 - 0.000000081 * t * t3;
        }
        else if (t < 0)
        {
            deltat = -0.000278 + 0.000265 * t + 0.000262 * t2;
        }
        else
        {
            deltat = -0.000084 + 0.000265 * t + 0.000262 * t2;
        }

        double jdNew = jd1 + c1 - deltat + 0.5 + timeZone / 24.0;
        return (int)Math.Floor(jdNew);
    }

    private static int GetLunarMonth11(int year, double timeZone = TimeZone)
    {
        int off = SolarToJulianDay(31, 12, year) - 2415021;
        int k = (int)Math.Floor(off / 29.530588853);
        int nm = GetNewMoonDay(k, timeZone);
        int sunTerm = GetSunLongitude(nm, timeZone);
        if (sunTerm >= 9)
        {
            nm = GetNewMoonDay(k - 1, timeZone);
        }
        return nm;
    }

    private static int GetLeapMonthOffset(int a11, double timeZone = TimeZone)
    {
        int k = (int)Math.Floor((a11 - 2415021.076998695) / 29.530588853 + 0.5);
        int last = 0;
        int i = 1;
        int arc = GetSunLongitude(GetNewMoonDay(k + i, timeZone), timeZone);
        do
        {
            last = arc;
            i++;
            arc = GetSunLongitude(GetNewMoonDay(k + i, timeZone), timeZone);
        } while (arc != last && i < 14);

        return i - 1;
    }

    /// <summary>
    /// Chuyển đổi từ ngày Âm lịch sang ngày Dương lịch (DateTime).
    /// </summary>
    public static DateTime ConvertLunarToSolar(int lunarDay, int lunarMonth, int lunarYear, bool isLeap = false)
    {
        int targetK;
        if (lunarMonth < 11)
        {
            int a11 = GetLunarMonth11(lunarYear - 1, TimeZone);
            int b11 = GetLunarMonth11(lunarYear, TimeZone);
            int off = 0;

            if (b11 - a11 > 365)
            {
                int leapOff = GetLeapMonthOffset(a11, TimeZone);
                int leapMonth = leapOff - 2;
                if (leapMonth < 0) leapMonth += 12;

                if (isLeap || lunarMonth > leapMonth)
                {
                    off = 1;
                }
            }

            int k = (int)Math.Floor((a11 - 2415021.076998695) / 29.530588853 + 0.5);
            targetK = k + lunarMonth + 1 + off;
        }
        else
        {
            int a11 = GetLunarMonth11(lunarYear, TimeZone);
            int k = (int)Math.Floor((a11 - 2415021.076998695) / 29.530588853 + 0.5);
            targetK = k + lunarMonth - 11;
        }

        int targetNewMoon = GetNewMoonDay(targetK, TimeZone);
        int targetJd = targetNewMoon + lunarDay - 1;
        return JulianDayToSolar(targetJd);
    }
}
