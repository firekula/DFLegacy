using DFLegacy.Server;

internal static class CreatureWarehouseMigrationSmokeTests
{
    internal static void Run(Action<bool,string> check)
    {
        using var fixture=new CreatureGiftFixture();
        var pet=new CharacterItemRecord(0,63003,7,CreatureStomach:73,CreatureExperience:1234,
            CreatureLevel:12,CreatureName:"旧宠",CreatureNoCharge:true,CreatureStomachRemainderSeconds:17,
            InstanceId:Guid.NewGuid());
        var egg=new CharacterItemRecord(1,63007,9,SealState:1,CreatureStomach:100,
            CreatureExperience:0,CreatureLevel:1,CreatureName:"旧蛋",CreatureNoCharge:false,InstanceId:Guid.NewGuid());
        var normalized=CharacterInventoryPlanner.NormalizeWarehouse([pet,egg],fixture.Items,120);
        check(normalized.Warehouse.Count==2 && normalized.OverflowItems.Count==0
            && normalized.Warehouse.GetValueOrDefault((ushort)0)==pet && normalized.Warehouse.GetValueOrDefault((ushort)1)==egg,
            "warehouse retains two pet instances and their UIDs instead of expanding UID into quantity");
        var collision=CharacterInventoryPlanner.NormalizeWarehouse([pet,egg with {Slot=0}],fixture.Items,2);
        check(collision.Warehouse.Count==2 && collision.OverflowItems.Count==0
            && collision.Warehouse.GetValueOrDefault((ushort)0)==pet && collision.Warehouse.GetValueOrDefault((ushort)1)==egg,
            "warehouse relocation preserves all pet state when legacy slots collide");
        var refused=false;
        try {CharacterInventoryPlanner.NormalizeWarehouse([pet,egg],fixture.Items,1);}
        catch(InvalidDataException){refused=true;}
        check(refused,"pet overflow refuses lossy ordinary-mail conversion before persistence");
        var stack=CharacterInventoryPlanner.NormalizeWarehouse([new(0,24,1495)],fixture.Items,120);
        check(stack.Warehouse.Count==2 && stack.Warehouse[0].CountOrValue==1000 && stack.Warehouse[1].CountOrValue==495,
            "ordinary creature food quantities still split at the real stack limit");
    }
}
