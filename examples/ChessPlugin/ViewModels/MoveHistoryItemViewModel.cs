using CommunityToolkit.Mvvm.ComponentModel;

namespace FryPdf.Plugin.Chess.ViewModels;

public partial class MoveHistoryItemViewModel : ObservableObject
{
    public int MoveNumber { get; }

    [ObservableProperty]
    private string _whiteMove = "";

    [ObservableProperty]
    private string _blackMove = "";

    public MoveHistoryItemViewModel(int moveNumber, string whiteMove = "", string blackMove = "")
    {
        MoveNumber = moveNumber;
        WhiteMove = whiteMove;
        BlackMove = blackMove;
    }
}
