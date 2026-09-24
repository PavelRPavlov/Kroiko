#nullable enable
using System.Collections.Generic;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using Kroiko.Domain;

namespace ATAFurniture.Server.Models;

// The Server's editable contact info: the text fields bind to it, ConverterContext re-raises its changes,
// and it carries the signed-in user's Email (test-email recipient and "CompanyEmail" in order emails).
// The domain only gets the contacts written into the order files (ADR-0004 §6).
public sealed class ContactInfoModel : INotifyPropertyChanged
{
    private string? _companyName;
    private string? _mobileNumber;
    private string? _email;

    public string? CompanyName
    {
        get => _companyName;
        set => SetField(ref _companyName, value);
    }

    public string? MobileNumber
    {
        get => _mobileNumber;
        set => SetField(ref _mobileNumber, value);
    }

    public string? Email
    {
        get => _email;
        set => SetField(ref _email, value);
    }

    public ContactInfo ToContactInfo() => new(CompanyName, MobileNumber);

    public event PropertyChangedEventHandler? PropertyChanged;

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }

    private bool SetField<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value)) return false;
        field = value;
        OnPropertyChanged(propertyName);
        return true;
    }
}
