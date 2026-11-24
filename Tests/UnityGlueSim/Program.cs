using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using TheLastKing.Tests;

namespace TheLastKing.Sim
{
    public static class Program
    {
        public static int Main(string[] args)
        {
            Console.OutputEncoding = System.Text.Encoding.UTF8;
            var filters = args.Where(a => a != "-v" && a != "list").ToList();
            if (args.Contains("-v")) TestServer.Verbose = true;
            Scenarios.Register();
            IEnumerable<TestCase> sel = Registry.Tests;
            if (filters.Count > 0) sel = sel.Where(t => filters.Any(f => t.Name.IndexOf(f, StringComparison.OrdinalIgnoreCase) >= 0));
            var tests = sel.ToList();
            if (args.Contains("list")) { foreach (var t in tests) Console.WriteLine(t.Name); return 0; }

            int passed = 0, failed = 0;
            var total = Stopwatch.StartNew();
            foreach (TestCase t in tests)
            {
                T.SetCurrent(t.Name);
                int before = T.Failures;
                var sw = Stopwatch.StartNew();
                Console.WriteLine("▶ " + t.Name);
                try { t.Body(); }
                catch (Exception ex) { T.Fail("ngoại lệ không bắt: " + ex); }
                bool ok = T.Failures == before;
                if (ok) passed++; else failed++;
                Console.WriteLine((ok ? "  ✓ " : "  ✗ ") + t.Name + "  (" + sw.ElapsedMilliseconds + " ms)");
            }
            Console.WriteLine();
            Console.WriteLine("=====================================================");
            Console.WriteLine((failed == 0 ? "PASS" : "FAIL") + ": " + passed + "/" + tests.Count + " kịch bản, " + T.Checks + " khẳng định, " + T.Failures + " lỗi, " + total.Elapsed.TotalSeconds.ToString("F1") + " s");
            foreach (string f in T.FailureLog) Console.WriteLine("  - " + f);
            return failed == 0 ? 0 : 1;
        }
    }
}
