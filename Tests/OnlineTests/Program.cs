using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;

namespace TheLastKing.Tests
{
    public static class Program
    {
        public static int Main(string[] args)
        {
            Console.OutputEncoding = System.Text.Encoding.UTF8;
            if (args.Length > 0 && args[0] == "bot") return BotCli.Run(args.Skip(1).ToArray());

            bool list = false;
            var filters = new List<string>();
            foreach (string a in args)
            {
                if (a == "list") list = true;
                else if (a == "--verbose" || a == "-v") TestServer.Verbose = true;
                else filters.Add(a);
            }

            ProtocolTests.Register();
            ServerFlowTests.Register();
            SecurityTests.Register();
            ResilienceTests.Register();
            TlsTests.Register();
            LoadTests.Register();

            IEnumerable<TestCase> selected = Registry.Tests;
            if (filters.Count > 0) selected = selected.Where(t => filters.Any(f => t.Name.IndexOf(f, StringComparison.OrdinalIgnoreCase) >= 0));
            var tests = selected.ToList();

            if (list) { foreach (var t in tests) Console.WriteLine(t.Name); return 0; }

            var total = Stopwatch.StartNew();
            int passed = 0, failed = 0;
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
            Console.WriteLine((failed == 0 ? "PASS" : "FAIL") + ": " + passed + "/" + tests.Count + " test, " + T.Checks + " khẳng định, " + T.Failures + " lỗi, " + total.Elapsed.TotalSeconds.ToString("F1") + " s");
            foreach (string f in T.FailureLog) Console.WriteLine("  - " + f);
            return failed == 0 ? 0 : 1;
        }
    }
}
