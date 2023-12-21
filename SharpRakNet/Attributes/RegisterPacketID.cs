using System;

public class RegisterPacketID : Attribute {
    public readonly int ID;

    public RegisterPacketID(int ID) {
        this.ID = ID;
    }
}