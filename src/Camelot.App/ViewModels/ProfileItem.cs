using Camelot.Core;

namespace Camelot.App.ViewModels;

/// <summary>Строка в списке прокси.</summary>
public sealed class ProfileItem(ProxyProfile profile) : ObservableObject
{
    private bool _isActive;

    public ProxyProfile Profile { get; private set; } = profile;

    public string Name => Profile.Name;

    public string Address => Profile.DisplayAddress;

    public string ProtocolTitle => Profile.ProtocolTitle;

    public bool IsActive
    {
        get => _isActive;
        set => Set(ref _isActive, value);
    }

    public void Update(ProxyProfile profile)
    {
        Profile = profile;
        Raise(nameof(Name));
        Raise(nameof(Address));
        Raise(nameof(ProtocolTitle));
    }
}
