using System.Linq;
using Playnite.SDK;
using Playnite.SDK.Models;

namespace SaveTracker.Resources.Helpers
{
    public class Misc
    {
        public static Game GetSelectedGame(IPlayniteAPI api)
        {
            // Get currently selected game from main view
            var selectedGames = api.MainView.SelectedGames;
    
            if (selectedGames != null && selectedGames.Any())
            {
                return selectedGames.First();
            }
    
            return null;
        }

    }
}