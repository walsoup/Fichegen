using Windows.Storage;
using Windows.Storage.Pickers;
using WinRT.Interop;

namespace FicheGen.App.Services;

public sealed class PickerService
{
    private nint _windowHandle;

    public void Initialize(nint windowHandle)
    {
        _windowHandle = windowHandle;
    }

    public async Task<StorageFile?> PickSingleFileAsync(params string[] fileTypes)
    {
        var picker = new FileOpenPicker();
        if (_windowHandle != nint.Zero)
        {
            InitializeWithWindow.Initialize(picker, _windowHandle);
        }

        picker.ViewMode = PickerViewMode.List;
        foreach (var type in fileTypes)
        {
            picker.FileTypeFilter.Add(type);
        }

        return await picker.PickSingleFileAsync();
    }

    public async Task<StorageFolder?> PickFolderAsync()
    {
        var picker = new FolderPicker();
        if (_windowHandle != nint.Zero)
        {
            InitializeWithWindow.Initialize(picker, _windowHandle);
        }

        picker.ViewMode = PickerViewMode.List;
        picker.FileTypeFilter.Add("*");

        return await picker.PickSingleFolderAsync();
    }

    public async Task<StorageFile?> PickSaveFileAsync(string suggestedName, IDictionary<string, IList<string>> fileTypes)
    {
        var picker = new FileSavePicker();
        if (_windowHandle != nint.Zero)
        {
            InitializeWithWindow.Initialize(picker, _windowHandle);
        }

        picker.SuggestedStartLocation = PickerLocationId.DocumentsLibrary;
        picker.SuggestedFileName = suggestedName;

        foreach (var kvp in fileTypes)
        {
            picker.FileTypeChoices.Add(kvp.Key, kvp.Value);
        }

        return await picker.PickSaveFileAsync();
    }
}
