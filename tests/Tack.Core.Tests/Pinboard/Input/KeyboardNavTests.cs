using Tack.Core.Pinboard.Input;

namespace Tack.Core.Tests.Pinboard.Input;

public class KeyboardNavTests
{
    const KeyModifiers None = KeyModifiers.None;

    static KeyboardCommand Press(BoardKey key, int focused = 1, int count = 4, KeyModifiers mods = None, bool repeat = false) =>
        KeyboardNav.Decide(key, mods, repeat, focused, count);

    static KeyboardCommand Move(int target) => new(KeyboardAction.Move, target);

    [Theory]
    [InlineData(0x25, BoardKey.Left)]
    [InlineData(0x4B, BoardKey.K)]
    [InlineData(0x26, BoardKey.None)]
    public void Virtual_keys_name_the_board_keys(int virtualKey, BoardKey key) =>
        Assert.Equal(key, KeyboardNav.KeyOf(virtualKey));

    [Fact]
    public void The_arrows_step_along_the_row_and_stop_at_its_ends()
    {
        Assert.Equal(Move(2), Press(BoardKey.Right, focused: 1));
        Assert.Equal(Move(0), Press(BoardKey.Left, focused: 1));
        Assert.Equal(Move(0), Press(BoardKey.Left, focused: 0));
        Assert.Equal(Move(3), Press(BoardKey.Right, focused: 3));
    }

    [Fact]
    public void With_nothing_focused_either_arrow_goes_to_the_first_print()
    {
        Assert.Equal(Move(0), Press(BoardKey.Right, focused: -1));
        Assert.Equal(Move(0), Press(BoardKey.Left, focused: -1));
    }

    [Fact]
    public void Home_and_End_go_to_the_row_s_ends()
    {
        Assert.Equal(Move(0), Press(BoardKey.Home, focused: 2));
        Assert.Equal(Move(3), Press(BoardKey.End, focused: -1));
    }

    [Fact]
    public void Moving_takes_Shift_but_not_Ctrl_Alt_or_the_Windows_key()
    {
        Assert.Equal(Move(2), Press(BoardKey.Right, mods: KeyModifiers.Shift));
        Assert.False(Press(BoardKey.Right, mods: KeyModifiers.Ctrl).Handled);
        Assert.False(Press(BoardKey.Home, mods: KeyModifiers.Alt).Handled);
        Assert.False(Press(BoardKey.End, mods: KeyModifiers.Win).Handled);
    }

    [Fact]
    public void An_empty_row_has_nowhere_to_move()
    {
        Assert.False(Press(BoardKey.Right, focused: -1, count: 0).Handled);
        Assert.False(Press(BoardKey.Home, focused: -1, count: 0).Handled);
    }

    [Fact]
    public void Enter_copies_and_Ctrl_Enter_opens()
    {
        Assert.Equal(KeyboardAction.Copy, Press(BoardKey.Enter).Action);
        Assert.Equal(KeyboardAction.Copy, Press(BoardKey.Enter, mods: KeyModifiers.Shift).Action);
        Assert.Equal(KeyboardAction.Open, Press(BoardKey.Enter, mods: KeyModifiers.Ctrl).Action);
        Assert.False(Press(BoardKey.Enter, mods: KeyModifiers.Alt).Handled);
        Assert.False(Press(BoardKey.Enter, mods: KeyModifiers.Ctrl | KeyModifiers.Win).Handled);
    }

    [Fact]
    public void K_keeps_once_per_press()
    {
        Assert.Equal(KeyboardAction.Keep, Press(BoardKey.K).Action);
        Assert.Equal(KeyboardAction.Keep, Press(BoardKey.K, mods: KeyModifiers.Shift).Action);
        Assert.False(Press(BoardKey.K, repeat: true).Handled);
        Assert.False(Press(BoardKey.K, mods: KeyModifiers.Ctrl).Handled);
    }

    [Fact]
    public void Delete_unpins_without_Ctrl()
    {
        Assert.Equal(KeyboardAction.Unpin, Press(BoardKey.Delete).Action);
        Assert.False(Press(BoardKey.Delete, mods: KeyModifiers.Ctrl).Handled);
    }

    [Fact]
    public void The_menu_key_and_Shift_F10_open_the_menu()
    {
        Assert.Equal(KeyboardAction.Menu, Press(BoardKey.Menu).Action);
        Assert.Equal(KeyboardAction.Menu, Press(BoardKey.Menu, mods: KeyModifiers.Shift).Action);
        Assert.Equal(KeyboardAction.Menu, Press(BoardKey.F10, mods: KeyModifiers.Shift).Action);
        Assert.False(Press(BoardKey.F10).Handled);
        Assert.False(Press(BoardKey.F10, mods: KeyModifiers.Shift | KeyModifiers.Ctrl).Handled);
    }

    [Theory]
    [InlineData(BoardKey.Enter)]
    [InlineData(BoardKey.K)]
    [InlineData(BoardKey.Delete)]
    [InlineData(BoardKey.Menu)]
    public void A_print_s_keys_need_a_focused_print(BoardKey key) =>
        Assert.False(Press(key, focused: -1).Handled);

    [Fact]
    public void Esc_tucks_whatever_is_held_even_on_an_empty_board()
    {
        Assert.Equal(KeyboardAction.Tuck, Press(BoardKey.Escape, focused: -1, count: 0).Action);
        Assert.Equal(KeyboardAction.Tuck, Press(BoardKey.Escape, mods: KeyModifiers.Ctrl).Action);
    }

    [Fact]
    public void Tab_stays_in_the_row_focusing_the_first_print_if_none_is()
    {
        Assert.Equal(Move(0), Press(BoardKey.Tab, focused: -1));
        Assert.Equal(KeyboardAction.Stay, Press(BoardKey.Tab, focused: 2).Action);
        Assert.Equal(KeyboardAction.Stay, Press(BoardKey.Tab, focused: 2, mods: KeyModifiers.Shift).Action);
        Assert.Equal(KeyboardAction.Stay, Press(BoardKey.Tab, focused: -1, count: 0).Action);
    }

    [Fact]
    public void Other_keys_are_left_alone() =>
        Assert.Equal(new KeyboardCommand(KeyboardAction.None), Press(BoardKey.None));

    [Theory]
    [InlineData(2, 5, 2)]
    [InlineData(0, 3, 0)]
    [InlineData(4, 4, 3)]
    [InlineData(1, 1, 0)]
    [InlineData(0, 0, -1)]
    public void A_removed_print_hands_the_focus_to_the_next_else_the_previous(int index, int remaining, int next) =>
        Assert.Equal(next, KeyboardNav.AfterRemoval(index, remaining));
}
