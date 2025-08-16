using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;

namespace AttendanceTool
{
    public partial class MainWindow : Window
    {
        public ObservableCollection<PunchDayView> PunchDays { get; } = new();

        private int _sumLeaveHrMinutes = 0;

        private double _sumLeaveDayDays = 0.0;

        public MainWindow()
        {
            InitializeComponent();
            PunchDaysList.ItemsSource = PunchDays;
        }

        private void BtnCalc_Click(object sender, RoutedEventArgs e)
        {
            var raw = TxtInput.Text ?? string.Empty;

            var punchDayViews = BuildPunchDayViews(raw);
            PunchDays.Clear();
            foreach (var day in punchDayViews) PunchDays.Add(day);

            var (leaveHourly, leaveDaily) = CalculateLeaves(raw);

            _sumLeaveHrMinutes = leaveHourly.Values.Sum();
            _sumLeaveDayDays = leaveDaily.Values.Sum();

            LblLeaveHrShort.Text = FormatHHmm(TimeSpan.FromMinutes(_sumLeaveHrMinutes));
            var tsLeaveDay = WorkHoursFromDays(_sumLeaveDayDays);
            LblLeaveDayShortDays.Text = _sumLeaveDayDays.ToString("0.##");
            LblLeaveDayShortHours.Text = FormatHHmm(tsLeaveDay);

            RecalculateSummaries();
        }

        private void BtnAddRow_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button btn && btn.Tag is PunchDayView day)
            {
                int nextPair = day.Entries.Count == 0 ? 1 : day.Entries.Last().PairNo;
                if (day.Entries.Count == 0 || day.Entries.Last().Kind == "خروج")
                    nextPair++;

                day.Entries.Add(new PunchEntry { PairNo = nextPair, Kind = "ورود", Time = "08:00" });
            }
        }

        private void BtnDeleteRow_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button btn && btn.Tag is PunchDayView day)
            {
                var grid = FindParentDataGrid(btn);
                if (grid?.SelectedItem is PunchEntry sel)
                    day.Entries.Remove(sel);
                else if (day.Entries.Count > 0)
                    day.Entries.RemoveAt(day.Entries.Count - 1);
            }
        }

        private void BtnAddMissingExit_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button btn && btn.Tag is PunchDayView day)
            {
                if (day.Entries.Count > 0 && day.Entries.Count % 2 == 1 && day.Entries.Last().Kind == "ورود")
                {
                    int pairNo = day.Entries.Last().PairNo;
                    var inMin = ParseHHmmToMinutes(day.Entries.Last().Time);
                    var outMin = Math.Max(inMin + 60, inMin);
                    day.Entries.Add(new PunchEntry
                    {
                        PairNo = pairNo,
                        Kind = "خروج",
                        Time = ToHHmm(Math.Min(outMin, 23 * 60 + 59))
                    });
                }
                else
                {
                    MessageBox.Show("آخرین جفت ناقص نیست یا آخرین ردیف «ورود» نیست.", "اطلاع", MessageBoxButton.OK, MessageBoxImage.Information);
                }
            }
        }

        private void BtnSaveDay_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button btn && btn.Tag is PunchDayView day)
            {
                if (!ValidateAndNormalizeDay(day, out string error))
                {
                    MessageBox.Show(error, "خطا در داده‌های روز", MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }

                day.Total = FormatHHmm(CalcDayTotal(day));
                day.PairCount = day.Entries.Select(en => en.PairNo).DefaultIfEmpty(0).Max();
                day.Warning = (day.Entries.Count % 2 == 1) ? "⚠️ جفت ناقص" : string.Empty;

                RecalculateSummaries();
            }
        }

        private void RecalculateSummaries()
        {
            var sumPunch = TimeSpan.Zero;
            foreach (var d in PunchDays)
                sumPunch += CalcDayTotal(d);
            LblSumPunch.Text = FormatHHmm(sumPunch);

            var sumLeaveHrTs = TimeSpan.FromMinutes(_sumLeaveHrMinutes);
            LblSumLeaveHr.Text = FormatHHmm(sumLeaveHrTs);

            var leaveDayTs = WorkHoursFromDays(_sumLeaveDayDays);
            LblSumLeaveDayDays.Text = _sumLeaveDayDays.ToString("0.##");
            LblSumLeaveDayHours.Text = FormatHHmm(leaveDayTs);

            var all = sumPunch + sumLeaveHrTs + leaveDayTs;
            LblSumAllHours.Text = FormatHHmm(all);
        }

        private static DataGrid? FindParentDataGrid(DependencyObject child)
        {
            DependencyObject? cur = child;
            while (cur != null && cur is not DataGrid)
                cur = System.Windows.Media.VisualTreeHelper.GetParent(cur);
            return cur as DataGrid;
        }

        public class PunchDayView : INotifyPropertyChanged
        {
            public string Date { get; set; } = "";
            public string Total { get; set; } = "";
            public int PairCount { get; set; }
            public string Warning { get; set; } = "";
            public ObservableCollection<PunchEntry> Entries { get; set; } = new();

            public event PropertyChangedEventHandler? PropertyChanged;

            protected void OnPropertyChanged(string name) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
        }

        public class PunchEntry : INotifyPropertyChanged
        {
            private int _pairNo;
            private string _kind = "ورود";
            private string _time = "08:00";

            public int PairNo
            { get => _pairNo; set { _pairNo = value; OnPropertyChanged(nameof(PairNo)); } }

            public string Kind
            { get => _kind; set { _kind = value; OnPropertyChanged(nameof(Kind)); } }

            public string Time
            { get => _time; set { _time = value; OnPropertyChanged(nameof(Time)); } }

            public string[] KindOptions { get; } = new[] { "ورود", "خروج" };

            public event PropertyChangedEventHandler? PropertyChanged;

            protected void OnPropertyChanged(string name) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
        }

        private static string NormalizeDigits(string input)
        {
            var map = new Dictionary<char, char>
            {
                ['۰'] = '0',
                ['۱'] = '1',
                ['۲'] = '2',
                ['۳'] = '3',
                ['۴'] = '4',
                ['۵'] = '5',
                ['۶'] = '6',
                ['۷'] = '7',
                ['۸'] = '8',
                ['۹'] = '9',
                ['٠'] = '0',
                ['١'] = '1',
                ['٢'] = '2',
                ['٣'] = '3',
                ['٤'] = '4',
                ['٥'] = '5',
                ['٦'] = '6',
                ['٧'] = '7',
                ['٨'] = '8',
                ['٩'] = '9'
            };
            var chars = input.ToCharArray();
            for (int i = 0; i < chars.Length; i++)
                if (map.TryGetValue(chars[i], out var r)) chars[i] = r;
            return new string(chars);
        }

        private static List<(string date, int hh, int mm)> ExtractAllDateTimes(string line)
        {
            var res = new List<(string, int, int)>();
            foreach (Match m in Regex.Matches(line, @"(?<d>\d{4}/\d{2}/\d{2})\s+(?<t>\d{2}:\d{2})"))
            {
                var date = m.Groups["d"].Value;
                var t = m.Groups["t"].Value.Split(':');
                int hh = int.Parse(t[0]);
                int mm = int.Parse(t[1]);
                res.Add((date, hh, mm));
            }
            return res;
        }

        private static bool TryExtractFirstDateTime(string line, out string date, out int hh, out int mm)
        {
            date = ""; hh = mm = 0;
            var all = ExtractAllDateTimes(line);
            if (all.Count == 0) return false;
            (date, hh, mm) = all[0];
            return true;
        }

        private static List<PunchDayView> BuildPunchDayViews(string rawInput)
        {
            var perDay = BuildForgottenPerDayRawMinutes(rawInput);
            var result = new List<PunchDayView>();

            foreach (var kv in perDay.OrderBy(k => k.Key))
            {
                var day = new PunchDayView { Date = kv.Key };
                var times = kv.Value.OrderBy(x => x).ToList();
                for (int i = 0; i < times.Count; i++)
                {
                    bool isEnter = (i % 2 == 0);
                    int pairNo = (i / 2) + 1;
                    day.Entries.Add(new PunchEntry
                    {
                        PairNo = pairNo,
                        Kind = isEnter ? "ورود" : "خروج",
                        Time = ToHHmm(times[i])
                    });
                }
                day.PairCount = day.Entries.Select(en => en.PairNo).DefaultIfEmpty(0).Max();
                day.Warning = (day.Entries.Count % 2 == 1) ? "⚠️ جفت ناقص" : string.Empty;
                day.Total = FormatHHmm(CalcDayTotal(day));
                result.Add(day);
            }

            return result;
        }

        private static Dictionary<string, List<int>> BuildForgottenPerDayRawMinutes(string rawInput)
        {
            var raw = NormalizeDigits(rawInput);
            var lines = raw.Split(new[] { "\r\n", "\n" }, StringSplitOptions.RemoveEmptyEntries);

            var filtered = lines.Where(l => l.Contains("فراموشی ثبت تردد") && !l.Contains("حذف"));
            var perDay = new Dictionary<string, List<int>>();

            foreach (var line in filtered)
            {
                if (TryExtractFirstDateTime(line, out var date, out var hh, out var mm))
                {
                    if (!perDay.TryGetValue(date, out var list))
                    {
                        list = new List<int>();
                        perDay[date] = list;
                    }
                    perDay[date].Add(hh * 60 + mm);
                }
            }
            return perDay;
        }

        private static TimeSpan CalcDayTotal(PunchDayView day)
        {
            var mapped = day.Entries
                .Select(e => new { e.Kind, Min = ParseHHmmToMinutes(e.Time) })
                .OrderBy(x => x.Min)
                .ToList();

            int total = 0;
            for (int i = 0; i + 1 < mapped.Count; i++)
                if (mapped[i].Kind == "ورود" && mapped[i + 1].Kind == "خروج")
                    total += Math.Max(0, mapped[i + 1].Min - mapped[i].Min);

            return TimeSpan.FromMinutes(total);
        }

        private static bool ValidateAndNormalizeDay(PunchDayView day, out string error)
        {
            error = "";
            foreach (var e in day.Entries)
            {
                if (e.Kind != "ورود" && e.Kind != "خروج")
                {
                    error = "نوع باید «ورود» یا «خروج» باشد.";
                    return false;
                }
                if (!TryParseHHmm(e.Time, out int mm) || mm < 0 || mm > (23 * 60 + 59))
                {
                    error = "فرمت ساعت باید HH:mm و بین 00:00 تا 23:59 باشد.";
                    return false;
                }
            }

            var sorted = day.Entries
                .Select(e => new { Entry = e, Min = ParseHHmmToMinutes(e.Time) })
                .OrderBy(x => x.Min)
                .ToList();

            int pair = 0;
            bool expectingExit = false;
            foreach (var x in sorted)
            {
                if (!expectingExit)
                {
                    x.Entry.Kind = "ورود"; pair++;
                    x.Entry.PairNo = pair; expectingExit = true;
                }
                else
                {
                    x.Entry.Kind = "خروج";
                    x.Entry.PairNo = pair; expectingExit = false;
                }
            }

            day.Warning = (sorted.Count % 2 == 1) ? "⚠️ جفت ناقص" : string.Empty;
            return true;
        }

        // Parse/Format helpers
        private static bool TryParseHHmm(string s, out int minutes)
        {
            minutes = 0;
            var m = Regex.Match(s ?? "", @"^\s*(\d{1,2}):(\d{2})\s*$");
            if (!m.Success) return false;
            int h = int.Parse(m.Groups[1].Value);
            int mm = int.Parse(m.Groups[2].Value);
            if (h < 0 || h > 23 || mm < 0 || mm > 59) return false;
            minutes = h * 60 + mm;
            return true;
        }

        private static int ParseHHmmToMinutes(string s) => TryParseHHmm(s, out var m) ? m : 0;

        private static string ToHHmm(int minutes) => $"{minutes / 60:00}:{minutes % 60:00}";

        private static string FormatHHmm(TimeSpan ts) => ToHHmm((int)Math.Round(ts.TotalMinutes));

        private static TimeSpan WorkHoursFromDays(double days) => TimeSpan.FromMinutes(days * 8 * 60.0);

        private static (Dictionary<string, int> leaveHourlyMinutesByDate,
                        Dictionary<string, double> leaveDailyDaysByStartDate)
            CalculateLeaves(string rawInput)
        {
            var raw = NormalizeDigits(rawInput);
            var lines = raw.Split(new[] { "\r\n", "\n" }, StringSplitOptions.RemoveEmptyEntries);

            var hourlyLines = lines.Where(l => l.Contains("مرخصی ساعتی") && !l.Contains("حذف"));
            var hourlyByDate = new Dictionary<string, int>();

            foreach (var line in hourlyLines)
            {
                var dts = ExtractAllDateTimes(line);
                if (dts.Count >= 2)
                {
                    var (d1, h1, m1) = dts[0];
                    var (d2, h2, m2) = dts[1];
                    int startMin = h1 * 60 + m1;
                    int endMin = h2 * 60 + m2;

                    if (d1 == d2)
                    {
                        int minutes = Math.Max(0, endMin - startMin);
                        if (!hourlyByDate.ContainsKey(d1)) hourlyByDate[d1] = 0;
                        hourlyByDate[d1] += minutes;
                    }
                    else
                    {
                        int day1Minutes = Math.Max(0, (24 * 60) - startMin);
                        int day2Minutes = Math.Max(0, endMin);
                        if (!hourlyByDate.ContainsKey(d1)) hourlyByDate[d1] = 0;
                        hourlyByDate[d1] += day1Minutes;
                        if (!hourlyByDate.ContainsKey(d2)) hourlyByDate[d2] = 0;
                        hourlyByDate[d2] += day2Minutes;
                    }
                }
            }

            var dailyLines = lines.Where(l => l.Contains("مرخصی استحقاقی") && !l.Contains("حذف"));
            var dailyByStartDate = new Dictionary<string, double>();

            foreach (var line in dailyLines)
            {
                var dts = ExtractAllDateTimes(line);
                if (dts.Count >= 2)
                {
                    var (startDate, sh, sm) = dts[0];
                    var (endDate, eh, em) = dts[1];

                    int ToSerialMinutes(string date, int h, int m)
                    {
                        var parts = date.Split('/');
                        int y = int.Parse(parts[0]);
                        int mo = int.Parse(parts[1]);
                        int d = int.Parse(parts[2]);
                        int serialDays = ((y * 12) + mo) * 31 + d;
                        return serialDays * 24 * 60 + h * 60 + m;
                    }

                    int startSerial = ToSerialMinutes(startDate, sh, sm);
                    int endSerial = ToSerialMinutes(endDate, eh, em);
                    int diffMin = Math.Max(0, endSerial - startSerial);

                    double days = diffMin / (24.0 * 60.0);
                    if (!dailyByStartDate.ContainsKey(startDate)) dailyByStartDate[startDate] = 0;
                    dailyByStartDate[startDate] += days;
                }
            }

            return (hourlyByDate, dailyByStartDate);
        }
    }
}