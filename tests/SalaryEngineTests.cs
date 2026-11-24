using System;
using SalaryStopwatch;

class SalaryEngineTests
{
    static int assertions;
    static void Check(bool condition, string name)
    { assertions++; if (!condition) throw new Exception("FAIL: " + name); }
    static void Near(decimal actual, decimal expected, string name)
    { Check(Math.Abs(actual - expected) < 0.00001m, name + ": " + actual); }
    static void Main()
    {
        Settings s = new Settings { DailyPay = 800m };
        DateTime monday = new DateTime(2026, 11, 23);
        Check(SalaryEngine.Validate(s) == null, "default settings valid");
        Near(SalaryEngine.Calculate(s, monday.AddHours(8)).Earned, 0m, "before shift");
        Near(SalaryEngine.Calculate(s, monday.AddHours(9)).Earned, 0m, "shift start");
        Near(SalaryEngine.Calculate(s, monday.AddHours(12)).Earned, 300m, "before lunch");
        Near(SalaryEngine.Calculate(s, monday.AddHours(12.5)).Earned, 300m, "lunch frozen");
        Near(SalaryEngine.Calculate(s, monday.AddHours(13)).Earned, 300m, "lunch end");
        Near(SalaryEngine.Calculate(s, monday.AddHours(14)).Earned, 400m, "half day");
        Near(SalaryEngine.Calculate(s, monday.AddHours(18)).Earned, 800m, "full day exact");
        Near(SalaryEngine.Calculate(s, monday.AddHours(23)).Earned, 800m, "capped after shift");
        Near(SalaryEngine.Calculate(s, monday.AddDays(1).AddHours(8)).Earned, 0m, "new day reset");
        Near(SalaryEngine.Calculate(s, new DateTime(2026, 11, 22, 14, 0, 0)).Earned, 0m, "rest day");
        Near(SalaryEngine.Calculate(s, monday.AddHours(10)).PerSecond, 800m / 28800m, "second rate");
        s.LunchEnabled = false;
        Near(SalaryEngine.Calculate(s, monday.AddHours(13.5)).Earned, 400m, "without lunch");
        s.Start = "22:00"; s.End = "06:00";
        Near(SalaryEngine.Calculate(s, monday.AddDays(1).AddHours(2)).Earned, 400m, "overnight halfway");
        Near(SalaryEngine.Calculate(s, new DateTime(2026, 11, 28, 2, 0, 0)).Earned, 400m, "Friday overnight into rest day");
        Check(SalaryEngine.Calculate(s, monday.AddDays(1).AddHours(6)).State == "今日已下班", "overnight retains completed shift");
        Near(SalaryEngine.Calculate(s, monday.AddDays(1).AddHours(6)).Earned, 800m, "overnight exact end");
        Near(SalaryEngine.Calculate(s, monday.AddDays(1).AddHours(22)).Earned, 0m, "overnight resets at next shift");
        s.LunchEnabled = true; s.LunchStart = "02:00"; s.LunchEnd = "03:00";
        Check(SalaryEngine.Validate(s) == null, "overnight break valid");
        Near(SalaryEngine.Calculate(s, monday.AddDays(1).AddHours(2.5)).Earned, 800m * 4m / 7m, "overnight break frozen");
        s.LunchStart = "23:30"; s.LunchEnd = "00:30";
        Check(SalaryEngine.Validate(s) == null, "break crosses midnight valid");
        Near(SalaryEngine.Calculate(s, monday.AddDays(1)).Earned, 800m * 1.5m / 7m, "midnight break frozen");
        s.LunchStart = "07:00"; s.LunchEnd = "08:00";
        Check(SalaryEngine.Validate(s) != null, "outside shift break rejected");
        s.LunchEnabled = false; s.End = "22:00";
        Check(SalaryEngine.Validate(s) != null, "zero shift rejected");
        s.End = "06:00"; s.Workdays = new int[0];
        Check(SalaryEngine.Validate(s) != null, "no workday rejected");
        s.Workdays = new[] { 1 }; s.DailyPay = 0;
        Check(SalaryEngine.Validate(s) != null, "zero pay rejected");
        TimeSpan time;
        Check(!SalaryEngine.TryTime("24:00", out time), "24 hour boundary invalid");
        Check(!SalaryEngine.TryTime("abc", out time), "invalid time rejected");
        Check(SalaryEngine.TryTime("9:00", out time), "single digit hour accepted");
        foreach (string value in new[] { "10:00", "10：00", "10: 00", "10： 00", " 10 ： 00 ", "10\u3000：\u300000" })
            Check(SalaryEngine.TryTime(value, out time) && time == TimeSpan.FromHours(10), "colon and whitespace accepted: " + value);
        Check(!SalaryEngine.TryTime(null, out time), "null time rejected");
        Check(!SalaryEngine.TryTime(" ", out time), "blank time rejected");
        Check(!SalaryEngine.TryTime("24：00", out time), "invalid fullwidth time rejected");
        Check(!SalaryEngine.TryTime("10：：00", out time), "duplicate colon rejected");
        Check(!SalaryEngine.TryTime("10：60", out time), "invalid minutes rejected");
        Settings mixed = new Settings { DailyPay = 600m, Start = "10： 00", End = "18: 30", LunchStart = "11： 45", LunchEnd = "13: 45" };
        Check(SalaryEngine.Validate(mixed) == null, "mixed colons valid in every time field");
        Snapshot mixedSnapshot = SalaryEngine.Calculate(mixed, monday.AddHours(15.25));
        Near(mixedSnapshot.Earned, 300m, "mixed colon schedule earns correct amount");
        Near(mixedSnapshot.PerSecond, 600m / 23400m, "mixed colon schedule correct rate");
        Check(SalaryEngine.Money(1.999m) == "1.99", "earned cents truncated");
        Check(SalaryEngine.Duration(0) == "00:00:00", "zero countdown");
        Check(SalaryEngine.Duration(-5) == "00:00:00", "negative countdown clamped");
        Console.WriteLine("PASS: " + assertions + " assertions");
    }
}
