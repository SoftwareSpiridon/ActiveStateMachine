using System.Collections.Immutable;
using ActiveStateMachine.Generators;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Xunit;

namespace ActiveStateMachine.Tests;

public class GeneratorDiagnosticsTests
{
    private const string Preamble = """
        using System.Threading.Tasks;
        using ActiveStateMachine.Attributes;

        public enum PhoneState { OffHook, Ringing }
        public enum PhoneTrigger { CallDialed, HungUp }
        """;

    [Fact]
    public void Valid_async_class_generates_source_without_diagnostics()
    {
        var (diagnostics, generated) = RunGenerator(Preamble + """

            [ActiveObjectAsync(typeof(PhoneState), typeof(PhoneTrigger))]
            public partial class Phone
            {
                partial void ConfigureStateMachine() { }

                [StateTrigger("PhoneTrigger.CallDialed")]
                public partial Task DialAsync(string number);

                [StateTrigger("PhoneTrigger.HungUp")]
                public partial Task HangUpAsync();
            }
            """);

        Assert.Empty(diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error));
        Assert.Contains(generated, s => s.Contains("TriggerDialAsync"));
        Assert.Contains(generated, s => s.Contains("public partial global::System.Threading.Tasks.Task HangUpAsync()"));
        Assert.Contains(generated, s => s.Contains("System.Threading.Channels.Channel"));
    }

    [Fact]
    public void Valid_sync_class_generates_thread_based_source_without_diagnostics()
    {
        var (diagnostics, generated) = RunGenerator(Preamble + """

            [ActiveObjectSync(typeof(PhoneState), typeof(PhoneTrigger))]
            public partial class Phone
            {
                partial void ConfigureStateMachine() { }

                [StateTrigger("PhoneTrigger.CallDialed")]
                public partial void Dial(string number);

                [StateTrigger("PhoneTrigger.HungUp")]
                public partial void HangUp();
            }
            """);

        Assert.Empty(diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error));
        string phone = Assert.Single(generated, s => s.Contains("class DialMessage"));
        Assert.Contains("System.Collections.Concurrent.BlockingCollection", phone);
        Assert.Contains("System.Threading.Thread", phone);
        Assert.Contains("public partial void HangUp()", phone);
        // No async / Task-returning API and no Channel in the synchronous implementation.
        Assert.DoesNotContain("System.Threading.Channels", phone);
        Assert.DoesNotContain("async ", phone);
    }

    [Fact]
    public void Marker_attributes_are_auto_emitted_into_the_compilation()
    {
        // A bare compilation with no attribute usage and no attributes reference at all.
        var (diagnostics, generated) = RunGenerator("public class Empty { }");

        Assert.Empty(diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error));

        // The generator still injects the marker attributes via post-initialization output.
        string attributes = Assert.Single(generated, s => s.Contains("class ActiveObjectAsyncAttribute"));
        Assert.Contains("namespace ActiveStateMachine.Attributes", attributes);
        Assert.Contains("internal sealed class ActiveObjectAsyncAttribute", attributes);
        Assert.Contains("internal sealed class ActiveObjectSyncAttribute", attributes);
        Assert.Contains("internal sealed class StateTriggerAttribute", attributes);
    }

    [Fact]
    public void Non_partial_class_reports_ASM001()
    {
        var (diagnostics, _) = RunGenerator(Preamble + """

            [ActiveObjectAsync(typeof(PhoneState), typeof(PhoneTrigger))]
            public class Phone
            {
                [StateTrigger("PhoneTrigger.HungUp")]
                public partial Task HangUpAsync();
            }
            """);

        Assert.Contains(diagnostics, d => d.Id == "ASM001");
    }

    [Fact]
    public void Async_non_task_trigger_method_reports_ASM002()
    {
        var (diagnostics, _) = RunGenerator(Preamble + """

            [ActiveObjectAsync(typeof(PhoneState), typeof(PhoneTrigger))]
            public partial class Phone
            {
                [StateTrigger("PhoneTrigger.HungUp")]
                public partial void HangUp();
            }
            """);

        Assert.Contains(diagnostics, d => d.Id == "ASM002");
    }

    [Fact]
    public void Too_many_parameters_reports_ASM003()
    {
        var (diagnostics, _) = RunGenerator(Preamble + """

            [ActiveObjectAsync(typeof(PhoneState), typeof(PhoneTrigger))]
            public partial class Phone
            {
                [StateTrigger("PhoneTrigger.CallDialed")]
                public partial Task DialAsync(int a, int b, int c, int d);
            }
            """);

        Assert.Contains(diagnostics, d => d.Id == "ASM003");
    }

    [Fact]
    public void Non_partial_trigger_method_reports_ASM004()
    {
        var (diagnostics, _) = RunGenerator(Preamble + """

            [ActiveObjectAsync(typeof(PhoneState), typeof(PhoneTrigger))]
            public partial class Phone
            {
                [StateTrigger("PhoneTrigger.HungUp")]
                public Task HangUpAsync() => Task.CompletedTask;
            }
            """);

        Assert.Contains(diagnostics, d => d.Id == "ASM004");
    }

    [Fact]
    public void Sync_non_void_trigger_method_reports_ASM006()
    {
        var (diagnostics, _) = RunGenerator(Preamble + """

            [ActiveObjectSync(typeof(PhoneState), typeof(PhoneTrigger))]
            public partial class Phone
            {
                [StateTrigger("PhoneTrigger.HungUp")]
                public partial Task HangUpAsync();
            }
            """);

        Assert.Contains(diagnostics, d => d.Id == "ASM006");
    }

    private static (ImmutableArray<Diagnostic> Diagnostics, string[] Generated) RunGenerator(string source)
    {
        // Note: no reference to any attributes assembly — the generator injects the marker
        // attributes into the compilation itself, which is exactly what these tests exercise.
        var references = ((string)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES")!)
            .Split(Path.PathSeparator)
            .Where(p => p.Length > 0)
            .Select(p => (MetadataReference)MetadataReference.CreateFromFile(p))
            .Append(MetadataReference.CreateFromFile(typeof(Stateless.StateMachine<,>).Assembly.Location))
            .ToList();

        var compilation = CSharpCompilation.Create(
            "TestAssembly",
            new[] { CSharpSyntaxTree.ParseText(source) },
            references,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

        var driver = CSharpGeneratorDriver.Create(new ActiveObjectGenerator());
        driver.RunGeneratorsAndUpdateCompilation(compilation, out var outputCompilation, out var diagnostics);

        var generated = outputCompilation.SyntaxTrees
            .Where(t => t.FilePath.EndsWith(".g.cs"))
            .Select(t => t.ToString())
            .ToArray();

        return (diagnostics, generated);
    }
}
