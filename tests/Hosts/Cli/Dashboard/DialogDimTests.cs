using Basil.Host.Cli.Dashboard.Shell;
using XenoAtom.Terminal.UI.Controls;

namespace Basil.Host.Cli.Tests.Dashboard;

[Collection(HostedTerminalCollection.Name)]
public sealed class DialogDimTests
{
	[Fact]
	public void Dim_WithNoDialogOpen_LetsALeftClickReachTheButtonUnderIt()
	{
		var clicked = 0;
		var page = new Button("Go").Click(() => clicked++);

		HostedTerminal.Run(new Dialogs().Dim(page), [(1, 0)]);

		Assert.Equal(1, clicked);
	}
}
