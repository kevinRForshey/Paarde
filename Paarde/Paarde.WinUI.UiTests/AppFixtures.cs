// Keyboard tests need the app in front, and two apps launching at once would fight over it.
[assembly: CollectionBehavior(DisableTestParallelization = true)]

namespace Paarde.WinUI.UiTests;

// One launch per test class: starting the app (and WebView2) takes several seconds.
public sealed class EditorFocusedApp() : AppSession(flowWizardMode: false);

public sealed class FlowWizardApp() : AppSession(flowWizardMode: true);
