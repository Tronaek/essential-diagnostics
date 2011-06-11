﻿using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Xml.Linq;
using Essential.Diagnostics.Tests.Utility;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Essential.Diagnostics.Tests
{
    [TestClass]
    public class RollingFileTests
    {
        public TestContext TestContext { get; set; }

        [TestMethod]
        public void HandlesEventSentDirectly()
        {
            var mockFileSystem = new MockFileSystem();
            var listener = new RollingFileTraceListener(null);
            listener.FileSystem = mockFileSystem;

            listener.TraceEvent(null, "source", TraceEventType.Information, 1, "{0}-{1}", 2, "A");
            listener.Flush();

            Assert.AreEqual(1, mockFileSystem.OpenedItems.Count);
            var tuple0 = mockFileSystem.OpenedItems[0];
            // Process name for test runner is 'QTAgent32'
            StringAssert.StartsWith(tuple0.Item1, "QTAgent32-" + DateTimeOffset.Now.Year.ToString());
            var data = tuple0.Item2.GetBuffer();
            var output = Encoding.UTF8.GetString(data, 0, (int)tuple0.Item2.Length);
            Assert.AreEqual("source\tInformation\t1\t2-A\r\n", output);
        }

        [TestMethod]
        public void HandlesEventFromTraceSource()
        {
            var mockFileSystem = new MockFileSystem();
            TraceSource source = new TraceSource("rollingFile1Source");
            var listener = source.Listeners.OfType<RollingFileTraceListener>().First();
            listener.FileSystem = mockFileSystem;

            source.TraceEvent(TraceEventType.Warning, 2, "{0}-{1}", 3, "B");
            source.Flush(); // or have AutoFlush configured

            Assert.AreEqual(1, mockFileSystem.OpenedItems.Count);
            var tuple0 = mockFileSystem.OpenedItems[0];
            // Process name for test runner is 'QTAgent32'
            StringAssert.StartsWith(tuple0.Item1, "QTAgent32-" + DateTimeOffset.Now.Year.ToString());
            var output = Encoding.UTF8.GetString(tuple0.Item2.GetBuffer(), 0, (int)tuple0.Item2.Length);
            Assert.AreEqual("rollingFile1Source\tWarning\t2\t3-B\r\n", output);
        }

        [TestMethod]
        public void RollOverTest()
        {
            var mockFileSystem = new MockFileSystem();
            var listener = new RollingFileTraceListener("Log{DateTime:HHmmss}");
            listener.FileSystem = mockFileSystem;

            listener.TraceEvent(null, "souce", TraceEventType.Information, 1, "A");
            Thread.Sleep(TimeSpan.FromSeconds(2));
            listener.TraceEvent(null, "souce", TraceEventType.Information, 2, "B");
            listener.Flush();

            Assert.AreEqual(2, mockFileSystem.OpenedItems.Count);
        }

        [TestMethod]
        public void ConfigParametersLoadedCorrectly()
        {
            TraceSource source = new TraceSource("rollingFile2Source");
            var listener = source.Listeners.OfType<RollingFileTraceListener>().First();

            Assert.AreEqual("rollingFile2", listener.Name);
            Assert.AreEqual("{DateTime},{EventType},{Message}", listener.Template);
            Assert.AreEqual("Trace{DateTime:yyyyMMdd}.log", listener.FilePathTemplate);
        }

    }
}
