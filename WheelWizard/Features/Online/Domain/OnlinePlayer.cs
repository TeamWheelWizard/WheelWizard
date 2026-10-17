using WheelWizard.WiiManagement.MiiManagement.Domain.Mii;

namespace WheelWizard.Online.Domain;

public sealed record OnlinePlayer(string Name, string FriendCode, string Vr, Mii? Mii, bool IsOnline);
