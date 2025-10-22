using Microsoft.AspNetCore.SignalR;
using System.Threading.Tasks;

namespace ChessBoardApp.Hubs
{
    public class ChessHub : Hub
    {
        // Denna hub används för att skicka uppdateringar från servern till alla klienter.
        // Vi använder IHubContext från controller-sidan för att skicka meddelanden.
        // Här behöver vi inte lägga mycket logik — controller skickar uppdateringar.
    }
}
