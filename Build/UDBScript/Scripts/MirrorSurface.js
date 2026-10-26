/// <reference path="../udbscript.d.ts" />

`#version 5`;

`#name Mirror surface`;

`#description Vertically mirror selected surfaces.`;

`#scriptoptions

surfaceType
{
	description = "Affected surface type in 2D view";
	default = 0;
	type = 11; // Enum
	enumvalues {
		0 = "Floor";
		1 = "Ceiling";
		2 = "Both (Swap)";
	}
}

mirrorHeight
{
    description = "Mirror height";
    type = 3;
    default = true;
}

mirrorTextures
{
    description = "Mirror textures";
    type = 3;
    default = true;
}

mirrorSidedefTextures
{
    description = "Mirror sidedef textures";
    type = 3;
    default = false;
}

sharedAxis
{
    description = "Shared symmetry axis";
    type = 3;
    default = true;
}
`;

const surfaceType = UDB.ScriptOptions.surfaceType;
const useSharedAxis = UDB.ScriptOptions.sharedAxis;

const getSectorVertices = (sector) => {
    const vertices = [];

    sector.getSidedefs().forEach(side => {
        vertices.push(side.line.start);
        vertices.push(side.line.end);
    });

    return vertices;
}

const findLowestZOnPlane = (plane, sector) => {
    return Math.min(...getSectorVertices(sector).map(v =>
        plane.getZ([v.position.x, v.position.y])
    ));
};

const findHighestZOnPlane = (plane, sector) => {
    return Math.max(...getSectorVertices(sector).map(v =>
        plane.getZ([v.position.x, v.position.y])
    ));
};

const findAxis = (elements) => {
    let axis = null;

    elements.forEach(element => {
        let sector;
        const shapeSector = element.sector;
        if (['floor', 'ceiling', 'both'].includes(element.type)) {
            sector = shapeSector;
        } else {
            sector = element.controlLinedef.front.sector;
        }

        if (['floor', '3dfloor_bottom'].includes(element.type)) {
            axis = Math.min(axis ?? Number.MAX_SAFE_INTEGER, findLowestZOnPlane(sector.getCeilingPlane(), shapeSector));
        } else {
            axis = Math.max(axis ?? Number.MIN_SAFE_INTEGER, findHighestZOnPlane(sector.getFloorPlane(), shapeSector));
        }
    });

    return axis;
};

const getSelectedElements = () => {
    let visualSelection = UDB.Map.srb2_visualSelection;

    if (!visualSelection) {
        visualSelection = [];

        UDB.Map.getSelectedOrHighlightedSectors().forEach(sector => {
            sector.getTags().forEach(tag => {
                if (surfaceType === 0) {
                    visualSelection.push({ type: 'floor', sector });
                } else if (surfaceType === 1) {
                    visualSelection.push({ type: 'ceiling', sector });
                } else if (surfaceType === 2) {
                    visualSelection.push({ type: 'both', sector });
                }
            });
        });
    }

    return visualSelection.filter(e => ['floor', 'ceiling', 'both', '3dfloor_bottom', '3dfloor_top'].includes(e.type));
};

const elements = getSelectedElements();
if (elements.length === 0)
    UDB.die('No surfaces selected');

const sharedAxisZ = findAxis(elements);

elements.forEach(element => {
    let sector;
    const shapeSector = element.sector;
    if (['floor', 'ceiling', 'both'].includes(element.type)) {
        sector = shapeSector;
    } else {
        sector = element.controlLinedef.front.sector;
    }

    const axis = useSharedAxis ? sharedAxisZ : findAxis([element]);

    const fHeight = sector.floorHeight;
    const fPlane = sector.getFloorPlane();
    const fNormal = sector.getFloorSlope();
    const fOffset = sector.floorSlopeOffset;

    const cHeight = sector.ceilingHeight;
    const cPlane = sector.getCeilingPlane();

    const fTexture = sector.floorTexture;
    const cTexture = sector.ceilingTexture;

    const sectorFields = {};
    [
        'xpanningfloor', 'ypanningfloor', 'xpanningceiling', 'ypanningceiling',
        'xscalefloor', 'yscalefloor', 'xscaleceiling', 'yscaleceiling',
        'rotationfloor', 'rotationceiling',
    ].forEach(f => sectorFields[f] = sector.fields[f]);

    if (element.type === 'floor' || element.type === '3dfloor_bottom' || element.type === 'both') {
        if (UDB.ScriptOptions.mirrorHeight) {
            let mirroredPlane = new UDB.Plane([cPlane.normal.x, cPlane.normal.y, -cPlane.normal.z], cPlane.offset);
            mirroredPlane = Misc.getRaisedPlane(mirroredPlane, 2 * axis);

            sector.setFloorSlope(mirroredPlane.normal);
            sector.floorSlopeOffset = mirroredPlane.offset;

            sector.floorHeight = -cHeight + 2 * axis;
        }

        if (UDB.ScriptOptions.mirrorTextures) {
            sector.floorTexture = cTexture;

            sector.fields.xpanningfloor = sectorFields.xpanningceiling;
            sector.fields.ypanningfloor = sectorFields.ypanningceiling;

            sector.fields.xscalefloor = sectorFields.xscaleceiling;
            sector.fields.yscalefloor = sectorFields.yscaleceiling;

            sector.fields.rotationfloor = sectorFields.rotationceiling;
        }
    }

    if (element.type === 'ceiling' || element.type === '3dfloor_top' || element.type === 'both') {
        if (UDB.ScriptOptions.mirrorHeight) {
            let mirroredPlane = new UDB.Plane([fPlane.normal.x, fPlane.normal.y, -fPlane.normal.z], fPlane.offset);
            mirroredPlane = Misc.getRaisedPlane(mirroredPlane, 2 * axis);

            sector.setCeilingSlope(mirroredPlane.normal);
            sector.ceilingSlopeOffset = mirroredPlane.offset;

            sector.ceilingHeight = -fHeight + 2 * axis;
        }

        if (UDB.ScriptOptions.mirrorTextures) {
            sector.ceilingTexture = fTexture;

            sector.fields.xpanningceiling = sectorFields.xpanningfloor;
            sector.fields.ypanningceiling = sectorFields.ypanningfloor;

            sector.fields.xscaleceiling = sectorFields.xscalefloor;
            sector.fields.yscaleceiling = sectorFields.yscalefloor;

            sector.fields.rotationceiling = sectorFields.rotationfloor;
        }
    }

    if (UDB.ScriptOptions.mirrorSidedefTextures) {
        sector.getSidedefs().forEach(side => {
            const upperTexture = side.upperTexture;
            const lowerTexture = side.lowerTexture;

            const sideFields = {};
            [
                'offsetx_top', 'offsetx_bottom', 'offsety_top', 'offsety_bottom',
                'scalex_top', 'scalex_bottom', 'scaley_top', 'scaley_bottom',
                'light_top', 'light_bottom',
                'lightabsolute_top', 'lightabsolute_bottom',
            ].forEach(f => sideFields[f] = side.fields[f]);

            if (element.type === 'floor' || element.type === '3dfloor_bottom' || element.type === 'both') {
                side.lowerTexture = upperTexture;

                side.fields.offsetx_bottom = sideFields.offsetx_top;
                side.fields.offsety_bottom = sideFields.offsety_top;

                side.fields.scalex_bottom = sideFields.scalex_top;
                side.fields.scaley_bottom = sideFields.scaley_top;

                side.fields.light_bottom = sideFields.light_top;
                side.fields.lightabsolute_bottom = sideFields.lightabsolute_top;
            }

            if (element.type === 'ceiling' || element.type === '3dfloor_top' || element.type === 'both') {
                side.upperTexture = lowerTexture;

                side.fields.offsetx_top = sideFields.offsetx_bottom;
                side.fields.offsety_top = sideFields.offsety_bottom;

                side.fields.scalex_top = sideFields.scalex_bottom;
                side.fields.scaley_top = sideFields.scaley_bottom;

                side.fields.light_top = sideFields.light_bottom;
                side.fields.lightabsolute_top = sideFields.lightabsolute_bottom;
            }
        });
    }
});
