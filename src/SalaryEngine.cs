using System;
using System.Collections.Generic;
using System.Globalization;
using System.Runtime.Serialization;

namespace SalaryStopwatch
{
    [DataContract]
    public sealed class Settings
    {
        [DataMember] public decimal DailyPay = 300m;
        [DataMember] public string Start = "09:00";
        [DataMember] public string End = "18:00";
        [DataMember] public bool LunchEnabled = true;
        [DataMember] public string LunchStart = "12:00";
        [DataMember] public string LunchEnd = "13:00";
        [DataMember] public int[] Workdays = new[] { 1, 2, 3, 4, 5 };
        [DataMember] public bool Compact;
        [DataMember] public bool HideAmounts;
        [DataMember] public bool Pinned = true;
        [DataMember] public double Left = -1;
        [DataMember] public double Top = -1;
        [DataMember] public int DockEdge;
    }

    public sealed class WorkShift
    {
        public DateTime Date, Start, End;
        public DateTime? BreakStart, BreakEnd;
        public bool IsWorkday;
        public double PaidSeconds
        {
            get { return (End - Start).TotalSeconds - (BreakStart.HasValue ? (BreakEnd.Value - BreakStart.Value).TotalSeconds : 0); }
        }
    }

    public sealed class Snapshot
    {
        public WorkShift Shift;
        public decimal PerSecond, Earned;
        public double Elapsed, Remaining, Progress;
        public string State, Message;
        public bool Working;
    }

    public static class SalaryEngine
    {
        public static bool TryTime(string value, out TimeSpan time)
        {
            string normalized = (value ?? string.Empty).Replace('：', ':').Trim();
            string[] parts = normalized.Split(':');
            if (parts.Length == 2) normalized = parts[0].Trim() + ":" + parts[1].Trim();
            DateTime parsed;
            bool ok = DateTime.TryParseExact(normalized, new[] { "H:mm", "HH:mm" }, CultureInfo.InvariantCulture, DateTimeStyles.None, out parsed);
            time = ok ? parsed.TimeOfDay : TimeSpan.Zero;
            return ok;
        }

        public static string Validate(Settings settings)
        {
            if (settings.DailyPay <= 0 || settings.DailyPay > 100000000m) return "请输入大于 0、且不超过 1 亿的日薪。";
            TimeSpan start, end, lunchStart, lunchEnd;
            if (!TryTime(settings.Start, out start) || !TryTime(settings.End, out end)) return "上下班时间请填写为 09:00 或 09：00（24 小时制）。";
            if (start == end) return "上班和下班时间不能相同。";
            if (settings.Workdays == null || settings.Workdays.Length == 0) return "请至少选择一个工作日。";
            foreach (int day in settings.Workdays) if (day < 0 || day > 6) return "工作日设置无效。";
            if (settings.LunchEnabled)
            {
                if (!TryTime(settings.LunchStart, out lunchStart) || !TryTime(settings.LunchEnd, out lunchEnd)) return "午休时间请填写为 12:00 或 12：00（24 小时制）。";
                if (lunchStart == lunchEnd) return "午休开始和结束时间不能相同。";
                WorkShift shift = BuildShift(settings, new DateTime(2026, 1, 5));
                if (shift.BreakStart < shift.Start || shift.BreakEnd > shift.End || shift.PaidSeconds <= 0)
                    return "休息时段须在班次内，并保留有效工作时间。";
            }
            return null;
        }

        public static WorkShift BuildShift(Settings settings, DateTime date)
        {
            TimeSpan start, end;
            TryTime(settings.Start, out start);
            TryTime(settings.End, out end);
            WorkShift shift = new WorkShift { Date = date.Date, Start = date.Date + start, End = date.Date + end };
            if (shift.End <= shift.Start) shift.End = shift.End.AddDays(1);
            shift.IsWorkday = Array.IndexOf(settings.Workdays, (int)date.DayOfWeek) >= 0;
            if (settings.LunchEnabled)
            {
                TimeSpan b1, b2;
                TryTime(settings.LunchStart, out b1);
                TryTime(settings.LunchEnd, out b2);
                DateTime breakStart = date.Date + b1;
                if (breakStart < shift.Start) breakStart = breakStart.AddDays(1);
                DateTime breakEnd = breakStart.Date + b2;
                if (breakEnd <= breakStart) breakEnd = breakEnd.AddDays(1);
                shift.BreakStart = breakStart;
                shift.BreakEnd = breakEnd;
            }
            return shift;
        }

        public static Snapshot Calculate(Settings settings, DateTime now)
        {
            WorkShift shift = BuildShift(settings, now.Date);
            WorkShift previous = BuildShift(settings, now.Date.AddDays(-1));
            if (previous.IsWorkday && previous.End.Date > previous.Start.Date && now < shift.Start) shift = previous;
            double elapsed = Math.Max(0, Math.Min((now - shift.Start).TotalSeconds, (shift.End - shift.Start).TotalSeconds));
            if (shift.BreakStart.HasValue)
                elapsed -= Math.Max(0, Math.Min((now - shift.BreakStart.Value).TotalSeconds, (shift.BreakEnd.Value - shift.BreakStart.Value).TotalSeconds));
            elapsed = shift.IsWorkday ? Math.Max(0, Math.Min(elapsed, shift.PaidSeconds)) : 0;
            decimal rate = settings.DailyPay / (decimal)shift.PaidSeconds;
            Snapshot result = new Snapshot { Shift = shift, PerSecond = rate, Elapsed = elapsed, Remaining = shift.PaidSeconds - elapsed,
                Progress = elapsed / shift.PaidSeconds, Earned = Math.Min(settings.DailyPay, rate * (decimal)elapsed) };
            if (!shift.IsWorkday) { result.State = "今天休息"; result.Message = "好好生活，也是一件正经事。"; }
            else if (now < shift.Start) { result.State = "还未上班"; result.Message = "距离开工 " + Duration((shift.Start - now).TotalSeconds); }
            else if (now >= shift.End) { result.State = "今日已下班"; result.Message = "今天的努力已到账，好好休息。"; result.Earned = settings.DailyPay; }
            else if (shift.BreakStart.HasValue && now >= shift.BreakStart.Value && now < shift.BreakEnd.Value)
            { result.State = "休息时间"; result.Message = "休息不计薪，" + shift.BreakEnd.Value.ToString("HH:mm") + " 继续积累。"; }
            else { result.State = "正在计薪"; result.Message = "每一秒的努力，都算数。"; result.Working = true; }
            return result;
        }

        public static string Duration(double seconds)
        {
            int total = Math.Max(0, (int)Math.Ceiling(seconds));
            return string.Format("{0:00}:{1:00}:{2:00}", total / 3600, total / 60 % 60, total % 60);
        }

        public static string Money(decimal amount)
        {
            return (Math.Floor(amount * 100m) / 100m).ToString("N2", CultureInfo.GetCultureInfo("zh-CN"));
        }
    }
}
