using System.Collections.Generic;
using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace ATAFurniture.Server.Models;

public class MegaTradingGroupModel(string oldName, string newName) : INotifyPropertyChanged
{
    private string _oldName = oldName;
    private string _newName = newName;
    
    public string OldName 
    {
        get => _oldName;
        set => SetField(ref _oldName, value);
    }
    public string NewName 
    {
        get => _newName;
        set => SetField(ref _newName, value);
    }
    
    public event PropertyChangedEventHandler PropertyChanged;
    protected virtual void OnPropertyChanged([CallerMemberName] string propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
    protected bool SetField<T>(ref T field, T value, [CallerMemberName] string propertyName = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value))
            return false;

        field = value;
        OnPropertyChanged(propertyName);
        return true;
    }
}