using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace ATAFurniture.Server.Models;

public sealed class MegaTradingViewModel(bool rotated, string edgeBandingMaterial, string leftEdge, string rightEdge, string bottomEdge, string topEdge, Guid id, double width, double height, double thickness, int quantity, string material, string note)
    : INotifyPropertyChanged {
    private string _note = note;
    private string _material = material;
    private bool _rotated = rotated;
    private string _edgeBandingMaterial = edgeBandingMaterial;
    private string _leftEdge = leftEdge;
    private string _rightEdge = rightEdge;
    private string _bottomEdge = bottomEdge;
    private string _topEdge = topEdge;
    private Guid _id = id;
    private double _width = width;
    private double _height = height;
    private double _thickness = thickness;
    private int _quantity = quantity;

    public event PropertyChangedEventHandler PropertyChanged;
    private void OnPropertyChanged([CallerMemberName] string propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
    private bool SetField<T>(ref T field, T value, [CallerMemberName] string propertyName = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value)) return false;
        field = value;
        OnPropertyChanged(propertyName);
        return true;
    }
    public string Note 
    {
        get => _note;
        set => SetField(ref _note, value);
    }
    public string Material
    {
        get => _material;
        set => SetField(ref _material, value);
    }
    public string EdgeBandingMaterial 
    {
        get => _edgeBandingMaterial;
        set => SetField(ref _edgeBandingMaterial, value);
    }
    public bool Rotated
    {
        get => _rotated;
        set => SetField(ref _rotated, value);
    }
    public string LeftEdge 
    {
        get => _leftEdge;
        set => SetField(ref _leftEdge, value);
    }
    public string RightEdge 
    {
        get => _rightEdge;
        set => SetField(ref _rightEdge, value);
    }
    public string BottomEdge 
    {
        get => _bottomEdge;
        set => SetField(ref _bottomEdge, value);
    }
    public string TopEdge 
    {
        get => _topEdge;
        set => SetField(ref _topEdge, value);
    }
    public Guid Id 
    {
        get => _id;
        set => SetField(ref _id, value);
    }
    public double Width 
    {
        get => _width;
        set => SetField(ref _width, value);
    }
    public double Height 
    {
        get => _height;
        set => SetField(ref _height, value);
    }
    public double Thickness 
    {
        get => _thickness;
        set => SetField(ref _thickness, value);
    }
    public int Quantity 
    {
        get => _quantity;
        set => SetField(ref _quantity, value);
    }
    
}