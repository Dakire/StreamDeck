using System;
using System.IO;
using System.Linq;
using System.Reflection;
using Xunit;

namespace StreamDak.Tests;

/// <summary>
/// Les frontières entre couches sont vérifiées sur les assemblies compilés : le Core ne connaît ni WPF, ni Windows, ni la
/// plateforme ; la plateforme Windows ne connaît pas WPF ; seule la fenêtre du deck appelle Win32 dans l'interface.
/// </summary>
public class ArchitectureTests
{
    static readonly Assembly Core = typeof(DeckDocument).Assembly;
    static readonly Assembly Platform = typeof(Platform.Windows.WindowsPlatform).Assembly;
    static readonly Assembly App = typeof(App).Assembly;

    static string[] References(Assembly a) => a.GetReferencedAssemblies().Select(r => r.Name!).ToArray();

    static readonly string[] Ui = { "PresentationFramework", "PresentationCore", "WindowsBase", "System.Windows.Forms", "System.Xaml", "WPF-UI", "Wpf.Ui" };

    [Fact]
    public void TheCoreKnowsNeitherTheUiNorWindowsNorTheOtherLayers()
    {
        var refs = References(Core);
        Assert.Empty(refs.Intersect(Ui));
        Assert.DoesNotContain("StreamDak", refs);
        Assert.DoesNotContain("StreamDak.Platform.Windows", refs);
        Assert.DoesNotContain("Microsoft.Win32.Registry", refs);
        Assert.DoesNotContain(refs, r => r.StartsWith("Microsoft.Extensions.", StringComparison.Ordinal));
        Assert.DoesNotContain(refs, r => r.StartsWith("Serilog", StringComparison.Ordinal));
    }

    [Fact]
    public void TheCoreTargetsPlainNetAndCallsNoNativeCode()
    {
        Assert.DoesNotContain("-windows", Core.GetCustomAttribute<System.Runtime.Versioning.TargetFrameworkAttribute>()!.FrameworkName);
        Assert.Empty(NativeMethods(Core));
    }

    [Fact]
    public void TheWindowsPlatformKnowsNoUiFramework()
    {
        var refs = References(Platform);
        Assert.Empty(refs.Intersect(Ui));
        Assert.DoesNotContain("StreamDak", refs);
        Assert.Contains("StreamDak.Core", refs);
    }

    [Fact]
    public void OnlyWindowsPlatformIsPublicInThePlatformAssembly()
    {
        var exported = Platform.GetExportedTypes().Select(t => t.Name).OrderBy(n => n).ToArray();
        Assert.Equal(new[] { "AutoStart", "DpapiSecretStore", "WindowsPlatform" }, exported);   // le reste (SendInput, COM, fenêtres) est interne
    }

    [Fact]
    public void TheApplicationCallsWin32OnlyFromTheDeckWindow()
    {
        var owners = NativeMethods(App).Select(m => m.DeclaringType!.Name).Distinct().OrderBy(n => n).ToArray();
        Assert.Equal(new[] { "MainWindow" }, owners);   // NOACTIVATE, placement multi-écran : voir AGENTS.md
    }

    [Fact]
    public void ViewModelsDoNotDependOnWindowsOrTheContainerInternals()
    {
        var viewModels = App.GetTypes().Where(t => t.Namespace == "StreamDak.ViewModels").ToList();
        Assert.NotEmpty(viewModels);
        foreach (var t in viewModels)
        {
            var fields = t.GetFields(BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
            Assert.DoesNotContain(fields, f => typeof(IServiceProvider).IsAssignableFrom(f.FieldType));   // pas de localisateur de services
            Assert.DoesNotContain(fields, f => typeof(System.Windows.Window).IsAssignableFrom(f.FieldType)); // la fenêtre passe par une interface (IShell…)
        }
    }

    [Fact]
    public void TheStaticServiceLocatorIsGone()
    {
        Assert.Null(App.GetType("StreamDak.AppServices"));
    }

    /// <summary>
    /// Le toucher ne déplace jamais le curseur système et ne fabrique aucun clic (ADR 0013, 0036) : aucune source de l'application ne l'appelle. Un test sur le
    /// curseur réel serait aléatoire (la souris de l'utilisateur le bouge), ce garde-fou statique ne l'est pas. Le clavier simulé (<c>SendInput</c> de touches) reste permis.
    /// </summary>
    [Fact]
    public void NoSourceMovesTheSystemCursorOrInjectsAMouse()
    {
        var root = new DirectoryInfo(AppContext.BaseDirectory);
        while (root != null && !File.Exists(Path.Combine(root.FullName, "StreamDak.csproj"))) root = root.Parent;
        Assert.NotNull(root);
        string[] forbidden = { "SetCursorPos", "mouse_event", "SendMouseInput", "MOUSEEVENTF_", "INPUT_MOUSE", "SetPhysicalCursorPos", "ClipCursor" };
        var sep = Path.DirectorySeparatorChar;
        var hits = Directory.EnumerateFiles(root!.FullName, "*.cs", SearchOption.AllDirectories)
            .Where(f => !f.Contains($"{sep}obj{sep}") && !f.Contains($"{sep}bin{sep}") && !f.Contains($"{sep}tests{sep}") && !f.Contains($"{sep}artifacts{sep}") && !f.Contains($"{sep}samples{sep}"))
            .Select(f => (File: f, Text: File.ReadAllText(f)))
            .SelectMany(x => forbidden.Where(w => x.Text.Contains(w, StringComparison.Ordinal)).Select(w => $"{Path.GetRelativePath(root.FullName, x.File)} : {w}"))
            .ToList();
        Assert.True(hits.Count == 0, "appel interdit : " + string.Join(" ; ", hits));
    }

    static MethodInfo[] NativeMethods(Assembly a) => a.GetTypes()
        .SelectMany(t => t.GetMethods(BindingFlags.Static | BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly))
        .Where(m => (m.Attributes & MethodAttributes.PinvokeImpl) != 0)
        .ToArray();
}
