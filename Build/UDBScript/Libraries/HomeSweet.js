/// <reference path="../udbscript.d.ts" />

class HomeSweet {
    static getThingLinks(thing) {
        const arg0 = thing.fields['stringarg0'] ?? '';
        if (arg0 === '')
            return [];
        const links = arg0.split(' ').map(t => parseInt(t));
        if (links.includes(NaN))
            UDB.die(`Malformed string argument 1 for thing ${thing.index} (${arg0})`);
        return links.filter(l => l !== 0);
    }

    static setThingLinks(thing, links) {
        thing.fields['stringarg0'] = links.join(' ');
    }
}
