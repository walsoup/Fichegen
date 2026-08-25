using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.Linq;
using System.Threading.Tasks;
using FicheGen.App.Models;
using FicheGen.Core.Diff;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media.Animation;
using Windows.ApplicationModel.DataTransfer;
using Windows.System;

namespace FicheGen.App.Views.Controls;

public sealed class AssistantRequestEventArgs : EventArgs
{
    public AssistantRequestEventArgs(string prompt, string mode)
    {
        Prompt = prompt;
        Mode = mode;
    }

    public string Prompt { get; }

    public string Mode { get; }
}

public sealed class AssistantDiffEventArgs : EventArgs
{
    public AssistantDiffEventArgs(AssistantMessage message)
    {
        Message = message;
    }

    public AssistantMessage Message { get; }
}

/// <summary>
/// Conversation avec l'assistant IA, réponses progressives et cartes de diff.
/// </summary>
public sealed partial class AssistantPane : UserControl
{
    public static readonly DependencyProperty IsStreamingProperty =
        DependencyProperty.Register(
            nameof(IsStreaming),
            typeof(bool),
            typeof(AssistantPane),
            new PropertyMetadata(false, OnIsStreamingChanged));

    public static readonly DependencyProperty ProviderNameProperty =
        DependencyProperty.Register(
            nameof(ProviderName),
            typeof(string),
            typeof(AssistantPane),
            new PropertyMetadata("IA", OnProviderChanged));

    public static readonly DependencyProperty ModelNameProperty =
        DependencyProperty.Register(
            nameof(ModelName),
            typeof(string),
            typeof(AssistantPane),
            new PropertyMetadata("Modèle automatique", OnModelChanged));

    private string _selectedMode = "Auto";

    public AssistantPane()
    {
        InitializeComponent();

        MessagesItemsControl.ItemsSource = Messages;
        Messages.CollectionChanged += OnMessagesCollectionChanged;

        Loaded += OnLoaded;
        Unloaded += OnUnloaded;

        UpdateModeCaption();
        UpdateStreamingVisuals();
    }

    public ObservableCollection<AssistantMessage> Messages { get; } = [];

    public bool IsStreaming
    {
        get => (bool)GetValue(IsStreamingProperty);
        set => SetValue(IsStreamingProperty, value);
    }

    public string ProviderName
    {
        get => (string)GetValue(ProviderNameProperty);
        set => SetValue(ProviderNameProperty, value ?? string.Empty);
    }

    public string ModelName
    {
        get => (string)GetValue(ModelNameProperty);
        set => SetValue(ModelNameProperty, value ?? string.Empty);
    }

    public string SelectedMode => _selectedMode;

    public event EventHandler<AssistantRequestEventArgs>? SendRequested;

    public event EventHandler? StopRequested;

    public event EventHandler? NewConversationRequested;

    public event EventHandler<AssistantDiffEventArgs>? ApplyDiffRequested;

    public event EventHandler<AssistantDiffEventArgs>? RejectDiffRequested;

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        UpdateStreamingVisuals();
        ScrollToBottom();
    }

    private void OnUnloaded(object sender, RoutedEventArgs e)
    {
        TypingDotsStoryboard.Stop();
    }

    private static void OnIsStreamingChanged(
        DependencyObject dependencyObject,
        DependencyPropertyChangedEventArgs args)
    {
        ((AssistantPane)dependencyObject).UpdateStreamingVisuals();
    }

    private static void OnProviderChanged(
        DependencyObject dependencyObject,
        DependencyPropertyChangedEventArgs args)
    {
        var pane = (AssistantPane)dependencyObject;

        if (pane.ProviderBadgeText is not null)
        {
            pane.ProviderBadgeText.Text = args.NewValue as string ?? "IA";
        }
    }

    private static void OnModelChanged(
        DependencyObject dependencyObject,
        DependencyPropertyChangedEventArgs args)
    {
        var pane = (AssistantPane)dependencyObject;

        if (pane.ModelBadgeText is not null)
        {
            pane.ModelBadgeText.Text = args.NewValue as string ?? Services.L10n.Get("AP_ModelBadge.Text");
        }
    }

    private void UpdateStreamingVisuals()
    {
        if (SendButton is null || StopButton is null)
        {
            return;
        }

        SendButton.Visibility = IsStreaming
            ? Visibility.Collapsed
            : Visibility.Visible;

        StopButton.Visibility = IsStreaming
            ? Visibility.Visible
            : Visibility.Collapsed;

        PromptTextBox.IsEnabled = !IsStreaming;

        if (IsStreaming && IsLoaded)
        {
            TypingDotsStoryboard.Begin();
        }
        else
        {
            TypingDotsStoryboard.Stop();
        }

        UpdateSendButtonState();
        ScrollToBottom();
    }

    private void OnMessagesCollectionChanged(
        object? sender,
        NotifyCollectionChangedEventArgs e)
    {
        ScrollToBottom();
    }

    private void OnModeSelectionChanged(
        object sender,
        SelectionChangedEventArgs e)
    {
        if (ModeSegmented.SelectedItem is FrameworkElement item &&
            item.Tag is string mode)
        {
            _selectedMode = mode;
        }
        else
        {
            _selectedMode = ModeSegmented.SelectedIndex switch
            {
                1 => "Générer",
                2 => "Modifier",
                3 => "Question",
                _ => "Auto"
            };
        }

        UpdateModeCaption();
    }

    private void UpdateModeCaption()
    {
        if (ModeCaptionText is null || PromptTextBox is null)
        {
            return;
        }

        switch (_selectedMode)
        {
            case "Générer":
                ModeCaptionText.Text =
                    Services.L10n.Get("AP_ModeGenerate_Caption");

                PromptTextBox.PlaceholderText =
                    Services.L10n.Get("AP_ModeGenerate_Prompt");
                break;

            case "Modifier":
                ModeCaptionText.Text =
                    Services.L10n.Get("AP_ModeEdit_Caption");

                PromptTextBox.PlaceholderText =
                    Services.L10n.Get("AP_ModeEdit_Prompt");
                break;

            case "Question":
                ModeCaptionText.Text =
                    Services.L10n.Get("AP_ModeQuestion_Caption");

                PromptTextBox.PlaceholderText =
                    Services.L10n.Get("AP_ModeQuestion_Prompt");
                break;

            default:
                ModeCaptionText.Text =
                    Services.L10n.Get("AP_ModeAuto_Caption");

                PromptTextBox.PlaceholderText =
                    "Demandez une modification ou posez une question…";
                break;
        }
    }

    private void OnPromptTextChanged(
        object sender,
        TextChangedEventArgs e)
    {
        UpdateSendButtonState();
    }

    private void UpdateSendButtonState()
    {
        if (SendButton is null || PromptTextBox is null)
        {
            return;
        }

        SendButton.IsEnabled =
            !IsStreaming &&
            !string.IsNullOrWhiteSpace(PromptTextBox.Text);
    }

    private void OnPromptKeyDown(
        object sender,
        KeyRoutedEventArgs e)
    {
        if (e.Key != VirtualKey.Enter)
        {
            return;
        }

        var shiftState = Microsoft.UI.Input.InputKeyboardSource
            .GetKeyStateForCurrentThread(VirtualKey.Shift);

        var shiftPressed =
            (shiftState & Windows.UI.Core.CoreVirtualKeyStates.Down) != 0;

        if (shiftPressed)
        {
            return;
        }

        e.Handled = true;
        SendCurrentPrompt();
    }

    private void OnSendClicked(object sender, RoutedEventArgs e)
    {
        SendCurrentPrompt();
    }

    private void SendCurrentPrompt()
    {
        if (IsStreaming)
        {
            return;
        }

        var prompt = PromptTextBox.Text.Trim();

        if (string.IsNullOrWhiteSpace(prompt))
        {
            return;
        }

        // No local add: SendMessageAsync adds the user message to the VM and the
        // CollectionChanged handler mirrors it here — a local Add would duplicate it.
        PromptTextBox.Text = string.Empty;

        SendRequested?.Invoke(
            this,
            new AssistantRequestEventArgs(prompt, _selectedMode));
    }

    private void OnStopClicked(object sender, RoutedEventArgs e)
    {
        StopRequested?.Invoke(this, EventArgs.Empty);
    }

    private void OnClearClicked(object sender, RoutedEventArgs e)
    {
        Messages.Clear();
        IsStreaming = false;
        PromptTextBox.Text = string.Empty;

        NewConversationRequested?.Invoke(this, EventArgs.Empty);
    }

    private void OnChipClicked(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: string prompt })
        {
            return;
        }

        PromptTextBox.Text = prompt;
        PromptTextBox.Focus(FocusState.Programmatic);
        PromptTextBox.Select(PromptTextBox.Text.Length, 0);
        UpdateSendButtonState();
    }

    private void OnApplyDiffClicked(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { DataContext: AssistantMessage message })
        {
            ApplyDiffRequested?.Invoke(
                this,
                new AssistantDiffEventArgs(message));
        }
    }

    private void OnRejectDiffClicked(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { DataContext: AssistantMessage message })
        {
            message.IsDiffResolved = true;

            RejectDiffRequested?.Invoke(
                this,
                new AssistantDiffEventArgs(message));
        }
    }

    private async void OnCopyDiffClicked(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement
            {
                DataContext: AssistantMessage message
            })
        {
            return;
        }

        await CopyTextAsync(message.GetModifiedText());
    }

    private static Task CopyTextAsync(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return Task.CompletedTask;
        }

        var package = new DataPackage();
        package.SetText(text);

        Clipboard.SetContent(package);
        Clipboard.Flush();

        return Task.CompletedTask;
    }

    private void OnCaretLoaded(object sender, RoutedEventArgs e)
    {
        if (sender is not UIElement caret)
        {
            return;
        }

        var animation = new DoubleAnimation
        {
            From = 1,
            To = 0,
            Duration = TimeSpan.FromMilliseconds(500),
            AutoReverse = true,
            RepeatBehavior = RepeatBehavior.Forever
        };

        Storyboard.SetTarget(animation, caret);
        Storyboard.SetTargetProperty(animation, "Opacity");

        var storyboard = new Storyboard();
        storyboard.Children.Add(animation);
        storyboard.Begin();
    }

    private void ScrollToBottom()
    {
        if (!IsLoaded || MessageScrollViewer is null)
        {
            return;
        }

        DispatcherQueue.TryEnqueue(
            Microsoft.UI.Dispatching.DispatcherQueuePriority.Low,
            () =>
            {
                MessageScrollViewer.UpdateLayout();
                MessageScrollViewer.ChangeView(
                    null,
                    MessageScrollViewer.ScrollableHeight,
                    null,
                    true);
            });
    }

    // -----------------------------------------------------------------
    // API publique pour le streaming
    // -----------------------------------------------------------------

    public AssistantMessage BeginAssistantMessage()
    {
        var message = AssistantMessage.CreateAssistant(string.Empty);
        message.IsStreaming = true;

        Messages.Add(message);
        IsStreaming = true;

        return message;
    }

    public void AppendStreamingText(
        AssistantMessage message,
        string text)
    {
        if (message is null || string.IsNullOrEmpty(text))
        {
            return;
        }

        message.Content += text;
        ScrollToBottom();
    }

    public void CompleteAssistantMessage(AssistantMessage message)
    {
        if (message is not null)
        {
            message.IsStreaming = false;
        }

        IsStreaming = false;
        ScrollToBottom();
    }

    public void CompleteAssistantMessage(
        AssistantMessage message,
        IEnumerable<DiffLine> diffLines)
    {
        if (message is null)
        {
            return;
        }

        message.SetDiff(diffLines);
        CompleteAssistantMessage(message);
    }

    public void CancelStreaming(AssistantMessage? message = null)
    {
        if (message is not null)
        {
            message.IsStreaming = false;

            if (string.IsNullOrWhiteSpace(message.Content))
            {
                Messages.Remove(message);
            }
        }

        IsStreaming = false;
    }

    public void AddAssistantMessage(string content)
    {
        Messages.Add(AssistantMessage.CreateAssistant(content));
    }

    public void AddUserMessage(string content)
    {
        Messages.Add(AssistantMessage.CreateUser(content));
    }
}