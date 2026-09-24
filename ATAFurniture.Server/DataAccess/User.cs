#nullable enable
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Text.Json;
using ATAFurniture.Server.Models;

namespace ATAFurniture.Server.DataAccess;

// The Server's EF entity (credits, last selected branch); not a conversion concept (ADR-0004 §6).
// The migrations still name it "Kroiko.Domain.User": EF maps it to the same Users table and columns.
public class User : INotifyPropertyChanged
{
    private Guid _id;
    private string? _aadId;
    private int _creditsCount;
    private int _creditResets;
    private ManufacturerBranch? _lastSelectedCompany;
    private string? _name;
    private string? _email;
    private string? _mobileNumber;
    private string? _companyName;

    public Guid Id
    {
        get => _id;
        set => SetField(ref _id, value);
    }

    public string? AadId
    {
        get => _aadId;
        set => SetField(ref _aadId, value);
    }

    public int CreditsCount
    {
        get => _creditsCount;
        set => SetField(ref _creditsCount, value);
    }

    public int CreditResets
    {
        get => _creditResets;
        set => SetField(ref _creditResets, value);
    }

    public ManufacturerBranch? LastSelectedCompany
    {
        get => _lastSelectedCompany;
        set => SetField(ref _lastSelectedCompany, value);
    }

    public string? Name
    {
        get => _name;
        set => SetField(ref _name, value);
    }

    public string? Email
    {
        get => _email;
        set => SetField(ref _email, value);
    }

    public string? MobileNumber
    {
        get => _mobileNumber;
        set => SetField(ref _mobileNumber, value);
    }

    public string? CompanyName
    {
        get => _companyName;
        set => SetField(ref _companyName, value);
    }

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

    public override string ToString()
    {
        return JsonSerializer.Serialize(this);
    }
}