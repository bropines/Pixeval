#region Copyright (c) Pixeval/Pixeval
// GPL v3 License
// 
// Pixeval/Pixeval
// Copyright (c) 2023 Pixeval/LoginPage.xaml.cs
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
using System.Threading.Tasks;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media.Animation;
using Pixeval.AppManagement;
using Pixeval.Util.UI;
using Pixeval.Utilities;
using WinUI3Utilities;

namespace Pixeval.Pages.Login;

public sealed partial class LoginPage
{
    private readonly LoginPageViewModel _viewModel;

    public LoginPage()
    {
        _viewModel = new(this);
        InitializeComponent();
    }

    private void TokenLogin_OnTapped(object sender, object e) => RefreshToken(_viewModel.RefreshToken);

    private async void Login_OnTapped(object sender, object e) => await LoginAsync(false);

    private async void LoginWithNewAccount_OnTapped(object sender, RoutedEventArgs e) => await LoginAsync(true);

    private async Task LoginAsync(bool useNewAccount)
    {
        try
        {
            await _viewModel.WebView2LoginAsync(this, useNewAccount, Navigated);
        }
        catch (Exception exception)
        {
            _ = await this.CreateAcknowledgementAsync(LoginPageResources.ErrorWhileLoggingInTitle,
                LoginPageResources.ErrorWhileLogginInContentFormatted.Format(exception + "\n" + exception.StackTrace));
            _viewModel.CloseWindow();
        }

        return;
        void Navigated()
        {
            if (App.AppViewModel.MakoClient == null!)
                ThrowHelper.Exception();

            _ = DispatcherQueue.TryEnqueue(SuccessNavigating);
        }
    }

    private void LoginPage_OnLoaded(object sender, RoutedEventArgs e)
    {
        if (_viewModel.LogoutExit)
        {
            _viewModel.AdvancePhase(LoginPageViewModel.LoginPhaseEnum.WaitingForUserInput);
            _viewModel.IsFinished = _viewModel.IsEnabled = true;
        }
        else
        {
            RefreshToken(App.AppViewModel.LoginContext.RefreshToken);
        }
    }

    private async void RefreshToken(string refreshToken)
    {
        try
        {
            if (refreshToken.IsNotNullOrEmpty() && await _viewModel.RefreshAsync(refreshToken))
            {
                SuccessNavigating();
            }
            else
            {
                _viewModel.AdvancePhase(LoginPageViewModel.LoginPhaseEnum.WaitingForUserInput);
                _viewModel.IsFinished = _viewModel.IsEnabled = true;
            }
        }
        catch (Exception exception)
        {
            _ = await this.CreateAcknowledgementAsync(LoginPageResources.ErrorWhileLoggingInTitle,
                LoginPageResources.ErrorWhileLogginInContentFormatted.Format(exception.StackTrace));
            _viewModel.CloseWindow();
        }
    }

    private void SuccessNavigating()
    {
        _viewModel.AdvancePhase(LoginPageViewModel.LoginPhaseEnum.SuccessNavigating);
        NavigateParent<MainPage>(null, new DrillInNavigationTransitionInfo());
        _viewModel.LogoutExit = false;
        AppInfo.SaveContext();
    }

    private void LoginPage_OnUnloaded(object sender, RoutedEventArgs e)
    {
        _viewModel.Dispose();
    }
}
