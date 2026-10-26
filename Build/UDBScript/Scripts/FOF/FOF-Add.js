/// <reference path="../../udbscript.d.ts" />

`#version 5`;

`#name FOF - Add`;

`#description Add a new FOF inside the selected sectors`;

`#scriptoptions

interactive
{
    description = "Interactive (opens a window)";
    type = 3; // Boolean
    default = false;
}

type
{
    description = "Type";
    default = 100;
    type = 11; // Enum
	enumvalues {
		100 = "Solid";
		120 = "Water";
		150 = "Air Bobbing";
		160 = "Water Bobbing";
		170 = "Crumbling";
		190 = "Rising";
		200 = "Light Block";
		202 = "Fog Block";
		220 = "Intangible";
		223 = "Intangible, Invisible";
		250 = "Mario Block";
		251 = "Thwomp Block";
		254 = "Bustable Block";
		257 = "Quicksand";
		258 = "Laser";
		259 = "Custom";
	}
}

topTexture
{
    description = "Top texture";
    type = 6; // Texture
    default = "GFZROCK";
}

sideTexture
{
    description = "Side texture";
    type = 6; // Texture
    default = "GFZROCK";
}

bottomTexture
{
    description = "Bottom texture";
    type = 6; // Texture
    default = "GFZROCK";
}
`;

const typeEnum = {
    100: 'Solid',
    120: 'Water',
    150: 'Air Bobbing',
    160: 'Water Bobbing',
    170: 'Crumbling',
    190: 'Rising',
    200: 'Light Block',
    202: 'Fog Block',
    220: 'Intangible',
    223: 'Intangible, Invisible',
    250: 'Mario Block',
    251: 'Thwomp Block',
    254: 'Bustable Block',
    257: 'Quicksand',
    258: 'Laser',
    259: 'Custom',
};

const getRaisedSectorSlope = (slope, slopeOffset, delta) =>
    slopeOffset - slope.z * delta;

const queryOptions = () => {
    const options = new UDB.QueryOptions();

    options.addOption('type', 'Type', 11, UDB.ScriptOptions.type, typeEnum);
    // options.addOption('topTexture', 'Top texture', 6, UDB.ScriptOptions.topTexture);
    // options.addOption('sideTexture', 'Side texture', 6, UDB.ScriptOptions.sideTexture);
    // options.addOption('bottomTexture', 'Bottom texture', 6, UDB.ScriptOptions.bottomTexture);

    if (!options.query())
        UDB.exit();

    UDB.ScriptOptions.type = options.options.type;
    // UDB.ScriptOptions.topTexture = options.options.topTexture;
    // UDB.ScriptOptions.sideTexture = options.options.sideTexture;
    // UDB.ScriptOptions.bottomTexture = options.options.bottomTexture;
};

const setReasonableDefaultHeights = (baseElement, fofSector) => {
    const distanceFromSurface = 64;
    const height = 64;

    let baseZ, basePlane, direction;
    if (baseElement.type === '3dfloor_top') {
        baseZ = baseElement.controlLinedef.front.sector.ceilingHeight;
        basePlane = Misc.getReversedPlane(baseElement.controlLinedef.front.sector.getCeilingPlane());
        direction = 1;
    } else if (baseElement.type === '3dfloor_bottom') {
        baseZ = baseElement.controlLinedef.front.sector.floorHeight;
        basePlane = Misc.getReversedPlane(baseElement.controlLinedef.front.sector.getFloorPlane());
        direction = -1;
    } else if (baseElement.type === 'ceiling') {
        baseZ = baseElement.sector.ceilingHeight;
        basePlane = baseElement.sector.getCeilingPlane();
        direction = -1;
    } else {
        baseZ = baseElement.sector.floorHeight;
        basePlane = baseElement.sector.getFloorPlane();
        direction = 1;
    }

    const z1 = baseZ + distanceFromSurface * direction;
    const z2 = z1 + height * direction;

    const plane1 = Misc.getRaisedPlane(basePlane, distanceFromSurface * direction);
    const plane2 = Misc.getReversedPlane(Misc.getRaisedPlane(plane1, height * direction));

    if (direction === 1) {
        fofSector.floorHeight = z1;
        fofSector.ceilingHeight = z2;

        Misc.setSectorFloorPlane(fofSector, plane1);
        Misc.setSectorCeilingPlane(fofSector, plane2);
    } else {
        fofSector.floorHeight = z2;
        fofSector.ceilingHeight = z1;

        Misc.setSectorFloorPlane(fofSector, plane2);
        Misc.setSectorCeilingPlane(fofSector, plane1);
    }
}

const clearAction = (line) => {
    line.action = 0;

    for (let i = 0; i < line.args.length; i++)
        line.args[i] = 0;

    line.fields.stringarg0 = null;
    line.fields.stringarg1 = null;
};

const clearFields = (fields) => {
    Object.keys(fields)
        .filter(f => f !== 'udbscript_controlsector')
        .forEach(f => fields[f] = null);
}

const visualSelection = SRB2VisualMode.getSelectionWithFallBack({ highlighted: true });
if (visualSelection.length === 0)
    UDB.die('No sectors selected');

const controlSectors = SRB2ControlSector.sortControlSectors(SRB2ControlSector.findUnusedControlSectors());
if (controlSectors.length < visualSelection.length)
    UDB.die('Not enough control sectors available');

if (UDB.ScriptOptions.interactive)
    queryOptions();

UDB.Map.clearAllSelected();

visualSelection.forEach((element, i) => {
    let sector;
    if (element.type === '3dfloor_side')
        sector = element.sidedef.sector;
    else if (['floor', 'ceiling'].includes(element.type) || element.type.startsWith('3dfloor'))
        sector = element.sector;
    else
        UDB.die('One of the selected elements is not a sector surface or a sidedef');

    if (SRB2ControlSector.isFofSector(sector))
        return;

    const fofSector = controlSectors[i];
    const fofLines = fofSector.getSidedefs().map(s => s.line);
    const fofLine = SRB2ControlSector.findBestControlLine(fofLines);

    const tag = UDB.Map.getNewTag();

    sector.addTag(tag);

    fofLines.forEach(dirtyFofLine => {
        dirtyFofLine.clearFlags();
        dirtyFofLine.getTags().forEach(t => dirtyFofLine.removeTag(t));
        clearFields(dirtyFofLine.fields);
    });

    clearAction(fofLine);
    fofLine.action = UDB.ScriptOptions.type;
    fofLine.args[0] = tag;
    fofLine.args[1] = 255;

    fofSector.clearFlags();
    fofSector.getTags().forEach(t => fofSector.removeTag(t));
    clearFields(fofSector.fields);

    if (element.type.startsWith('3dfloor')) {
        const controlSide = element.controlLinedef.front;
        fofSector.ceilingTexture = controlSide.sector.ceilingTexture;
        fofLine.front.middleTexture = controlSide.middleTexture;
        fofSector.floorTexture = controlSide.sector.floorTexture;
    } else {
        let texture = (element.type === 'ceiling') ? sector.ceilingTexture : sector.floorTexture;
        fofSector.ceilingTexture = texture;
        fofLine.front.middleTexture = texture;
        fofSector.floorTexture = texture;
    }
    // fofSector.ceilingTexture = UDB.ScriptOptions.topTexture;
    // fofLine.front.middleTexture = UDB.ScriptOptions.sideTexture;
    // fofSector.floorTexture = UDB.ScriptOptions.bottomTexture;

    setReasonableDefaultHeights(element, fofSector);

    fofSector.brightness = sector.brightness

    fofSector.floorSelected = true;
    fofSector.ceilingSelected = true;
});
