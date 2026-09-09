namespace NovaChat.Server.Entities;

// Domain enums stay outside generated files. MariaDB INT columns scaffold as int;
// use integer enum constants in LINQ and cast back when creating API responses.
public enum ChatType
{
    Private = 0,
    Group = 1
}

public enum ChatMemberRole
{
    Member = 0,
    Admin = 1,
    Owner = 2
}
