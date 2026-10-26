/// <reference path="../../udbscript.d.ts" />

`#version 5`;

`#name Create FOF control sector`;

`#description Add a new FOF inside the selected sectors`;

`#scriptoptions

interactive
{
    description = "Interactive (opens a window)";
    type = 3;
    default = false;
}

topTexture
{
    description = "Top texture";
    type = 6;
    default = "GFZROCK";
}

sideTexture
{
    description = "Side texture";
    type = 6;
    default = "GFZROCK";
}

bottomTexture
{
    description = "Bottom texture";
    type = 6;
    default = "GFZROCK";
}
`;

const queryOptions = () => {
    const options = new UDB.QueryOptions();

    options.addOption('topTexture', 'Top texture', 6, UDB.ScriptOptions.topTexture);
    options.addOption('sideTexture', 'Side texture', 6, UDB.ScriptOptions.sideTexture);
    options.addOption('bottomTexture', 'Bottom texture', 6, UDB.ScriptOptions.bottomTexture);

    if (!options.query())
        UDB.exit();

    UDB.ScriptOptions.topTexture = options.options.topTexture;
    UDB.ScriptOptions.sideTexture = options.options.sideTexture;
    UDB.ScriptOptions.bottomTexture = options.options.bottomTexture;
};

const drawControlSector = (center) => {
    const len = 64;
    const x = center.x + len * Math.SQRT2 * 0.25;
    const y = center.y + len * Math.SQRT2 * 0.5;

    return UDB.Map.drawLines([
        [x, y],
        [x, y - len],
        [x - len, y - len],
        [x, y],
    ]);
};

const setReasonableDefaultHeights = (sector, fofSector) => {
    const distanceFromSurface = 64;
    const height = 64;

    fofSector.floorHeight = sector.floorHeight + distanceFromSurface;
    fofSector.ceilingHeight = fofSector.floorHeight + height;

    const bottomPlane = Misc.getRaisedPlane(sector.getFloorPlane(), distanceFromSurface);
    if (Math.abs(bottomPlane.normal.z - 1) > 0.001) {
        fofSector.setFloorSlope(bottomPlane.normal);
        fofSector.floorSlopeOffset = bottomPlane.offset;
    } else {
        fofSector.setFloorSlope([0, 0, 0]);
        fofSector.floorSlopeOffset = NaN;
    }

    const topPlane = Misc.getRaisedPlane(fofSector.getFloorPlane(), height);
    if (Math.abs(topPlane.normal.z - 1) > 0.001) {
        fofSector.setCeilingSlope(UDB.Vector3D.reversed(topPlane.normal));
        fofSector.ceilingSlopeOffset = -topPlane.offset;
    } else {
        fofSector.setCeilingSlope([0, 0, 0]);
        fofSector.ceilingSlopeOffset = NaN;
    }
};

const sectors = UDB.Map.getSelectedOrHighlightedSectors();
if (sectors.length === 0)
    UDB.die('No sectors selected');

if (!UDB.Map.mousePosition.isFinite())
    UDB.die('Mouse cursor not at a valid map position');

if (UDB.ScriptOptions.interactive)
    queryOptions();

UDB.Map.clearAllSelected();

if (!drawControlSector(UDB.Map.mousePosition))
    UDB.die('Failed to create control sector');
const fofSector = UDB.Map.getMarkedSectors()[0];
if (!fofSector)
    UDB.die('Failed to create control sector');
const fofLine = fofSector.getSidedefs()[0].line;

const tag = UDB.Map.getNewTag();

fofLine.action = 100;
fofLine.args[0] = tag;
fofLine.args[1] = 255;

fofSector.ceilingTexture = sectors[0].floorTexture;
fofLine.front.middleTexture = sectors[0].floorTexture;
fofSector.floorTexture = sectors[0].floorTexture;
// fofSector.ceilingTexture = UDB.ScriptOptions.topTexture;
// fofLine.front.middleTexture = UDB.ScriptOptions.sideTexture;
// fofSector.floorTexture = UDB.ScriptOptions.bottomTexture;

setReasonableDefaultHeights(sectors[0], fofSector);

fofSector.brightness = sectors[0].brightness

fofSector.floorSelected = true;
fofSector.ceilingSelected = true;

sectors.forEach((sector) => {
    if (SRB2ControlSector.isFofSector(sector))
        return;

    sector.addTag(tag);
});
