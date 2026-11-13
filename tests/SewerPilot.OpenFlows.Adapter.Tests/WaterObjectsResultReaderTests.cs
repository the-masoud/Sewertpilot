using System;
using System.Collections.Generic;
using NUnit.Framework;
using SewerPilot.OpenFlows.Contracts;
using SewerPilot.OpenFlows.Adapter;

namespace NUnit.Framework
{
    [AttributeUsage(AttributeTargets.Class)]
    public sealed class TestFixtureAttribute : Attribute { }

    [AttributeUsage(AttributeTargets.Method)]
    public sealed class TestAttribute : Attribute { }

    public static class Assert
    {
        public static T Throws<T>(Action action) where T : Exception
        {
            if (action == null) throw new ArgumentNullException(nameof(action));
            try
            {
                action();
            }
            catch (T ex)
            {
                return ex;
            }
            catch (Exception ex)
            {
                throw new InvalidOperationException($"Expected exception of type {typeof(T).Name}, but caught {ex.GetType().Name}: {ex.Message}", ex);
            }
            throw new InvalidOperationException($"Expected exception of type {typeof(T).Name}, but no exception was thrown.");
        }

        public static void That(object actual, IConstraint constraint)
        {
            if (constraint != null && !constraint.Apply(actual))
            {
                throw new InvalidOperationException($"Assertion failed: Expected {constraint.Description}, but was {actual}");
            }
        }
    }

    public interface IConstraint
    {
        bool Apply(object actual);
        string Description { get; }
    }

    public static class Is
    {
        public static class Not
        {
            public static IConstraint Null => new NotNullConstraint();
        }

        public static IConstraint EqualTo(object expected) => new EqualConstraint(expected);

        private sealed class NotNullConstraint : IConstraint
        {
            public bool Apply(object actual) => actual != null;
            public string Description => "not null";
        }

        private sealed class EqualConstraint : IConstraint
        {
            private readonly object _expected;
            public EqualConstraint(object expected) { _expected = expected; }
            public bool Apply(object actual)
            {
                if (actual == null && _expected == null) return true;
                if (actual == null || _expected == null) return false;
                if (actual.Equals(_expected)) return true;
                if (actual is IConvertible && _expected is IConvertible)
                {
                    try
                    {
                        return Convert.ToInt64(actual).Equals(Convert.ToInt64(_expected));
                    }
                    catch (Exception suppressedEx) { System.Diagnostics.Trace.TraceWarning("Suppressed non-fatal exception in WaterObjectsResultReaderTests.cs: " + suppressedEx.Message); }
                }
                return false;
            }
            public string Description => $"equal to {_expected}";
        }
    }

    public static class StringAssert
    {
        public static void Contains(string expected, string actual)
        {
            if (actual == null || !actual.Contains(expected))
            {
                throw new InvalidOperationException($"StringAssert.Contains failed: Expected string containing '{expected}', but was '{actual}'.");
            }
        }
    }
}

namespace SewerPilot.OpenFlows.Adapter.Tests
{
    public enum ElementCategory
    {
        Conduit,
        Junction
    }

    public interface IElementManager
    {
        ElementCategory Category { get; }
        IEnumerable<int> GetElementIds();
        string GetElementLabel(int id);
    }

    public sealed class MockElementManager : IElementManager
    {
        public ElementCategory Category { get; }
        private readonly int[] _ids;
        private readonly string[] _labels;

        public MockElementManager(ElementCategory category, int[] ids, string[] labels)
        {
            Category = category;
            _ids = ids ?? new int[0];
            _labels = labels ?? new string[0];
        }

        public IEnumerable<int> GetElementIds() => _ids;

        public string GetElementLabel(int id)
        {
            int idx = Array.IndexOf(_ids, id);
            return (idx >= 0 && idx < _labels.Length) ? _labels[idx] : null;
        }
    }

    public sealed class MockOpenFlowsModel : IOpenFlowsModel
    {
        public ModelIdentity Identity { get; }
        public bool IsOpen { get; }
        public string FilePath { get; }
        private readonly List<IElementManager> _managers;

        public MockOpenFlowsModel(bool isOpen = true, string filePath = "C:\\Models\\Lesson_1.stsw", List<IElementManager> managers = null)
        {
            Identity = new ModelIdentity(filePath ?? "test-model");
            IsOpen = isOpen;
            FilePath = filePath;
            _managers = managers ?? new List<IElementManager>();
        }

        public IEnumerable<IElementManager> GetActiveElementManagers() => _managers;

        public void Dispose() { }
    }

    public sealed class MockOpenFlowsRun
    {
        public string RunId { get; }

        public MockOpenFlowsRun(string runId)
        {
            RunId = runId;
        }

        public static implicit operator CalculationRunIdentity(MockOpenFlowsRun r) =>
            r != null ? new CalculationRunIdentity(r.RunId) : null;
    }

    [TestFixture]
    public sealed class WaterObjectsResultReaderTests
    {
        [Test]
        public void ReadElements_NullArguments_ThrowsArgumentNullException()
        {
            var reader = new WaterObjectsResultReader();
            var validModel = new MockOpenFlowsModel(isOpen: true);
            var validRun = new MockOpenFlowsRun("run-001");

            Assert.Throws<ArgumentNullException>(() => reader.ReadElements(null, validRun));
            Assert.Throws<ArgumentNullException>(() => reader.ReadElements(validModel, null));
        }

        [Test]
        public void ReadElements_ClosedModel_ThrowsInvalidOperationException()
        {
            var reader = new WaterObjectsResultReader();
            var closedModel = new MockOpenFlowsModel(isOpen: false, filePath: "C:\\Models\\Lesson_1.stsw");
            var validRun = new MockOpenFlowsRun("run-001");

            var ex = Assert.Throws<InvalidOperationException>(() => reader.ReadElements(closedModel, validRun));
            StringAssert.Contains("closed or uninitialized", ex.Message);
        }

        [Test]
        public void ReadElements_ZeroElementsDiscovered_FailsClosed()
        {
            var reader = new WaterObjectsResultReader();
            var emptyModel = new MockOpenFlowsModel(isOpen: true, managers: new List<IElementManager>());
            var validRun = new MockOpenFlowsRun("run-001");

            var ex = Assert.Throws<NotSupportedException>(() => reader.ReadElements(emptyModel, validRun));
            StringAssert.Contains("does not expose active element managers", ex.Message);
        }

        [Test]
        public void ReadElements_ValidModel_ReturnsPopulatedIdentities()
        {
            var reader = new WaterObjectsResultReader();
            var conduitManager = new MockElementManager(ElementCategory.Conduit, new[] { 101, 102 }, new[] { "CO-1", "CO-2" });
            var junctionManager = new MockElementManager(ElementCategory.Junction, new[] { 201 }, new[] { "MH-1" });
            
            var model = new MockOpenFlowsModel(isOpen: true, managers: new List<IElementManager> { conduitManager, junctionManager });
            var run = new MockOpenFlowsRun("run-001");

            var results = reader.ReadElements(model, run);

            Assert.That(results, Is.Not.Null);
            Assert.That(results.Count, Is.EqualTo(3));
            Assert.That(results[0].Id, Is.EqualTo(101));
            Assert.That(results[0].Label, Is.EqualTo("CO-1"));
            Assert.That(results[0].Category, Is.EqualTo(ElementCategory.Conduit));
            Assert.That(results[2].Id, Is.EqualTo(201));
            Assert.That(results[2].Label, Is.EqualTo("MH-1"));
            Assert.That(results[2].Category, Is.EqualTo(ElementCategory.Junction));
        }

        [Test]
        public void ReadElements_MissingLabel_FailsClosed()
        {
            var reader = new WaterObjectsResultReader();
            var unlabelledManager = new MockElementManager(ElementCategory.Conduit, new[] { 301 }, new string[] { null });
            var model = new MockOpenFlowsModel(isOpen: true, managers: new List<IElementManager> { unlabelledManager });
            var run = new MockOpenFlowsRun("run-001");

            var ex = Assert.Throws<InvalidOperationException>(() => reader.ReadElements(model, run));
            StringAssert.Contains("lacks an authoritative label", ex.Message);
        }

        public static void RunAll()
        {
            var fixture = new WaterObjectsResultReaderTests();
            fixture.ReadElements_NullArguments_ThrowsArgumentNullException();
            fixture.ReadElements_ClosedModel_ThrowsInvalidOperationException();
            fixture.ReadElements_ZeroElementsDiscovered_FailsClosed();
            fixture.ReadElements_ValidModel_ReturnsPopulatedIdentities();
            fixture.ReadElements_MissingLabel_FailsClosed();
        }
    }
}
