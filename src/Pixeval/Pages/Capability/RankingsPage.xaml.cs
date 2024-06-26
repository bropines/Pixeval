#region Copyright (c) Pixeval/Pixeval
// GPL v3 License
// 
// Pixeval/Pixeval
// Copyright (c) 2023 Pixeval/RankingsPage.xaml.cs
// 
// This program is free software: you can redistribute it and/or modify
// it under the terms of the GNU General Public License as published by
// the Free Software Foundation, either version 3 of the License, or
// (at your option) any later version.
// 
// This program is distributed in the hope that it will be useful,
// but WITHOUT ANY WARRANTY; without even the implied warranty of
// MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.  See the
// GNU General Public License for more details.
// 
// You should have received a copy of the GNU General Public License
// along with this program.  If not, see <http://www.gnu.org/licenses/>.
#endregion

using System;
using System.Collections.Generic;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;
using Pixeval.Controls;
using Pixeval.CoreApi.Global.Enum;
using Pixeval.Misc;
using Pixeval.Util;
using WinRT;
using WinUI3Utilities;

namespace Pixeval.Pages.Capability;

public sealed partial class RankingsPage : IScrollViewProvider
{
    private static readonly List<StringRepresentableItem> _illustrationRankOption = LocalizedResourceAttributeHelper.GetLocalizedResourceContents<RankOption>();

    private static readonly List<StringRepresentableItem> _novelRankOption =
        LocalizedResourceAttributeHelper.GetLocalizedResourceContents(
        [
            RankOption.Day,
            RankOption.Week,
            // RankOption.Month,
            RankOption.DayMale,
            RankOption.DayFemale,
            // RankOption.DayManga,
            // RankOption.WeekManga,
            // RankOption.MonthManga,
            RankOption.WeekOriginal,
            RankOption.WeekRookie,
            RankOption.DayR18,
            RankOption.DayMaleR18,
            RankOption.DayFemaleR18,
            RankOption.WeekR18,
            RankOption.WeekR18G,
            RankOption.DayAi,
            RankOption.DayR18Ai
        ]);

    public RankingsPage()
    {
        InitializeComponent();
        RankOptionComboBox.ItemsSource = _illustrationRankOption;
        RankOptionComboBox.SelectedItem = _illustrationRankOption[0];
        RankDateTimeCalendarDatePicker.Date = MaxDate;
    }

    public DateTime MaxDate => DateTime.Now.AddDays(-2);

    public override void OnPageActivated(NavigationEventArgs navigationEventArgs)
    {
        ChangeSource();
    }

    private void ComboBox_OnSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        RankOptionComboBox.ItemsSource = SimpleWorkTypeComboBox.GetSelectedItem<SimpleWorkType>() is SimpleWorkType.IllustAndManga
            ? _illustrationRankOption : _novelRankOption;
        RankOptionComboBox.SelectedItem = _illustrationRankOption[0];
        ChangeSource();
    }

    private void OnSelectionChanged(object sender, IWinRTObject e) => ChangeSource();

    private void ChangeSource()
    {
        var rankOption = RankOptionComboBox.SelectedItem.To<StringRepresentableItem>().Item.To<RankOption>();
        var dateTime = RankDateTimeCalendarDatePicker.Date!.Value.DateTime;
        WorkContainer.WorkView.ResetEngine(SimpleWorkTypeComboBox.GetSelectedItem<SimpleWorkType>() is SimpleWorkType.IllustAndManga
            ? App.AppViewModel.MakoClient.IllustrationRanking(rankOption, dateTime, App.AppViewModel.AppSettings.TargetFilter)
            : App.AppViewModel.MakoClient.NovelRanking(rankOption, dateTime, App.AppViewModel.AppSettings.TargetFilter));
    }

    public ScrollView ScrollView => WorkContainer.ScrollView;
}
