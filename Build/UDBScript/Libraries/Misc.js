/// <reference path="../udbscript.d.ts" />

class Misc {
    static getReversedPlane(plane) {
        return new UDB.Plane(UDB.Vector3D.reversed(plane.normal), -plane.offset);
    }

    static getRaisedPlane(plane, delta) {
        return new UDB.Plane(plane.normal, plane.offset - plane.normal.z * delta);
    }

    static setSectorFloorPlane(sector, plane) {
        if (Math.abs(plane.normal.z - 1) > 0.001) {
            sector.setFloorSlope(plane.normal);
            sector.floorSlopeOffset = plane.offset;
        } else {
            sector.setFloorSlope([0, 0, 0]);
            sector.floorSlopeOffset = NaN;
        }
    }

    static setSectorCeilingPlane(sector, plane) {
        if (Math.abs(plane.normal.z - (-1)) > 0.001) {
            sector.setCeilingSlope(plane.normal);
            sector.ceilingSlopeOffset = plane.offset;
        } else {
            sector.setCeilingSlope([0, 0, 0]);
            sector.ceilingSlopeOffset = NaN;
        }
    }
}
