using System.Runtime.CompilerServices;

// Lets the test projects host the app through WebApplicationFactory<Program>. Program.cs uses
// top-level statements, so the generated Program class is internal. Test-only; no runtime effect.
[assembly: InternalsVisibleTo("InpremClockingApp.Tests.Support")]
[assembly: InternalsVisibleTo("InpremClockingApp.Tests.Baseline")]
[assembly: InternalsVisibleTo("InpremClockingApp.Tests.Ui")]
[assembly: InternalsVisibleTo("Inprem.Platform.Tests.Integration")]
