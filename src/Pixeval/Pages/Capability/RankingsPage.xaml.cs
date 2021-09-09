﻿using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;
using System;
using System.Threading.Tasks;
using Pixeval.CoreApi.Global.Enum;
using Pixeval.Events;
using Pixeval.Options;
using Pixeval.Util;
using Pixeval.Util.Generic;

namespace Pixeval.Pages.Capability
{
    public sealed partial class RankingsPage
    {
        public RankingsPage()
        {
            InitializeComponent();
        }


        private DateTime MaxDate => DateTime.Now.AddDays(-2);

        public override void Dispose(NavigatingCancelEventArgs navigatingCancelEventArgs)
        {
            IllustrationContainer.ViewModel.Dispose();
        }

        public override void Prepare(NavigationEventArgs navigationEventArgs)
        {
            RankOptionComboBox.SelectedItem = RankOptionWrapper.AvailableOptions().Of(RankOption.Day);
            RankDateTimeCalendarDatePicker.Date = DateTime.Now.AddDays(-2);
            EventChannel.Default.Subscribe<MainPageFrameNavigatingEvent>(() => IllustrationContainer.ViewModel.FetchEngine?.Cancel());
        }

        private async void RankingsPage_OnLoaded(object sender, RoutedEventArgs e)
        {
            if (App.Window.GetNavigationModeAndReset() is not NavigationMode.Back)
            {
                await ChangeSource();
            }
        }

        private async void RankOptionComboBox_OnSelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            await ChangeSource();
        }
        
        private async void RankDateTimeCalendarDatePicker_OnDateChanged(CalendarDatePicker sender, CalendarDatePickerDateChangedEventArgs args)
        {
            await ChangeSource();
        }

        private async Task ChangeSource()
        {
            if (TryGetRankOption(RankOptionComboBox, out var rankOption) && TryGetDatetime(RankDateTimeCalendarDatePicker, out var dateTime))
            {
                await IllustrationContainer.ViewModel.ResetAndFill(App.MakoClient.Ranking(rankOption, dateTime));
            }
        }
        #region Helper Functions

        private bool TryGetDatetime(CalendarDatePicker sender, out DateTime dateTime)
        {
            if (sender is {Date: { } })
            {
                dateTime = sender.Date.Value.DateTime;
                return true;
            }

            dateTime = DateTime.Now;
            return false;
        }

        private bool TryGetRankOption(ComboBox sender, out RankOption option)
        {
            if (sender is {SelectedItem: RankOptionWrapper {Value: var t}})
            {
                option = t;
                return true;
            }

            option = RankOption.Day;
            return false;
        }
        
        #endregion
    }
}