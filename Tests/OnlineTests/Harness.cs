using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;

namespace TheLastKing.Tests
{
    /// <summary>Bộ khẳng định tối giản, an toàn đa luồng; không phụ thuộc gói ngoài.</summary>
    public static class T
    {
        static int checks, failures;
        [ThreadStatic] static string currentTest;
        static readonly List<string> failureLog = new List<string>();
        static readonly object gate = new object();

        public static int Checks { get { return checks; } }
        public static int Failures { get { return failures; } }
        public static IList<string> FailureLog { get { return failureLog; } }
        public static void SetCurrent(string name) { currentTest = name; }

        public static void Check(bool condition, string what)
        {
            Interlocked.Increment(ref checks);
            if (condition) return;
            Interlocked.Increment(ref failures);
            lock (gate) failureLog.Add((currentTest ?? "?") + ": " + what);
            Console.WriteLine("      ✗ " + what);
        }

        public static void Eq<V>(V expected, V actual, string what)
        {
            Check(EqualityComparer<V>.Default.Equals(expected, actual), what + " (mong đợi " + expected + ", nhận " + actual + ")");
        }

        public static void Fail(string what) { Check(false, what); }

        /// <summary>Bắt ngoại lệ kiểu TEx khi chạy hành động.</summary>
        public static void Throws<TEx>(Action a, string what) where TEx : Exception
        {
            try { a(); Check(false, what + " (không có ngoại lệ)"); }
            catch (TEx) { Check(true, what); }
            catch (Exception ex) { Check(false, what + " (sai kiểu ngoại lệ: " + ex.GetType().Name + ": " + ex.Message + ")"); }
        }

        public static bool WaitUntil(Func<bool> cond, int timeoutMs)
        {
            var sw = Stopwatch.StartNew();
            while (sw.ElapsedMilliseconds < timeoutMs)
            {
                if (cond()) return true;
                Thread.Sleep(2);
            }
            return cond();
        }
    }

    public sealed class TestCase
    {
        public string Name; public Action Body; public int Order;
    }

    public static class Registry
    {
        public static readonly List<TestCase> Tests = new List<TestCase>();
        public static void Add(string name, Action body) { Tests.Add(new TestCase { Name = name, Body = body, Order = Tests.Count }); }
    }
}
