# Closes the ring of skin GFL2 leaves out of this body under its belt, in every level of detail:
# a strip of triangles from the open edge at the top of the hips to the open edge at the bottom
# of the torso. The strip's lower vertices are copies of the hips' edge that take their texture
# coordinates from the torso's edge above them, so the strip is the belly's own colour throughout.
# usage: close_waist_ring.py <model.gltf> [--write]
import json, sys, math
import numpy as np
path = sys.argv[1]; write = '--write' in sys.argv
g = json.load(open(path)); binpath = path[:-5] + '.bin'
buf = bytearray(open(binpath, 'rb').read())
CT = {5126: np.float32, 5123: np.uint16, 5125: np.uint32, 5121: np.uint8}
NC = {'SCALAR': 1, 'VEC2': 2, 'VEC3': 3, 'VEC4': 4}
def acc(i):
    a = g['accessors'][i]; v = g['bufferViews'][a['bufferView']]
    off = v.get('byteOffset', 0) + a.get('byteOffset', 0)
    return np.frombuffer(bytes(buf), CT[a['componentType']], a['count'] * NC[a['type']], off).reshape(a['count'], NC[a['type']]).copy()
def add(array, like, target=None):
    while len(buf) % 4: buf.append(0)
    view = {'buffer': 0, 'byteOffset': len(buf), 'byteLength': array.nbytes}
    if target: view['target'] = target
    buf.extend(array.tobytes()); g['bufferViews'].append(view)
    a = dict(g['accessors'][like]); a.pop('byteOffset', None); a.pop('sparse', None)
    a['bufferView'] = len(g['bufferViews']) - 1; a['count'] = len(array) if array.ndim > 1 else array.size
    if 'min' in a:
        a['min'] = [float(x) for x in array.reshape(len(array), -1).min(axis=0)]; a['max'] = [float(x) for x in array.reshape(len(array), -1).max(axis=0)]
    g['accessors'].append(a); return len(g['accessors']) - 1
mats = [m['name'] for m in g['materials']]
def angle(p): return math.atan2(p[0], p[2]) % (2 * math.pi)
for mesh in g['meshes']:
    for prim in mesh['primitives']:
        if mats[prim['material']] != 'BodySkin':
            continue
        A = {k: acc(v) for k, v in prim['attributes'].items()}
        P = A['POSITION'].astype(np.float64); N = A['NORMAL'].astype(np.float64)
        I = acc(prim['indices']).reshape(-1, 3).astype(np.int64)
        key = {}; point = np.zeros(len(P), np.int64)
        for i, p in enumerate(np.round(P, 5)): point[i] = key.setdefault(tuple(p), len(key))
        edges = {}
        for t in I:
            for a, b in ((t[0], t[1]), (t[1], t[2]), (t[2], t[0])): edges.setdefault((point[a], point[b]), []).append((a, b))
        nxt = {}
        for (pa, pb), v in edges.items():
            if (pb, pa) not in edges: nxt.setdefault(pa, []).append((pb, v[0][0]))
        loops = []; seen = set()
        for start in list(nxt):
            loop = []; at = start
            while at not in seen and at in nxt:
                seen.add(at); to, a = nxt[at][0]; loop.append(a); at = to
            if len(loop) >= 8: loops.append(loop)
        def ring(lo, hi):
            found = []
            for l in loops:
                pts = P[l]; turn = np.unwrap(np.arctan2(pts[:, 0], pts[:, 2]))
                if abs(turn[-1] - turn[0]) > 0.9 * 2 * math.pi and lo < pts[:, 1].mean() < hi: found.append(l)
            assert len(found) == 1, (mesh['name'], lo, hi, len(found))
            return sorted(found[0], key=lambda i: angle(P[i]))
        low, high = ring(1.24, 1.29), ring(1.34, 1.39)
        # each of the hips' edge vertices again, with the texture coordinates of the torso's edge over it
        base = len(P); new = {k: [] for k in A}
        high_angles = np.array([angle(P[i]) for i in high])
        def over(i):
            d = np.abs(high_angles - angle(P[i])); d = np.minimum(d, 2 * math.pi - d); return high[int(d.argmin())]
        for i in low:
            j = over(i)
            for k in A: new[k].append(A[k][j] if k.startswith('TEXCOORD') else A[k][i])
        lows = list(range(base, base + len(low)))
        # zip the two rings together by angle
        tris = []; a = b = 0; na, nb = len(lows), len(high)
        la = [angle(P[i]) for i in low]; hb = [angle(P[i]) for i in high]
        while a < na or b < nb:
            ta = la[a % na] + (2 * math.pi if a >= na else 0); tb = hb[b % nb] + (2 * math.pi if b >= nb else 0)
            if b >= nb or (a < na and ta <= tb):
                tris.append((lows[a % na], lows[(a + 1) % na], high[b % nb])); a += 1
            else:
                tris.append((high[b % nb], lows[a % na], high[(b + 1) % nb])); b += 1
        allP = np.concatenate([P, np.array(new['POSITION'], np.float64)]); allN = np.concatenate([N, np.array(new['NORMAL'], np.float64)])
        fixed = []; flipped = 0
        for t in tris:
            face = np.cross(allP[t[1]] - allP[t[0]], allP[t[2]] - allP[t[0]])
            if np.dot(face, allN[list(t)].sum(axis=0)) < 0: t = (t[0], t[2], t[1]); flipped += 1
            fixed.append(t)
        area = sum(np.linalg.norm(np.cross(allP[t[1]] - allP[t[0]], allP[t[2]] - allP[t[0]])) / 2 for t in fixed)
        print(f"{mesh['name']}: hips edge {len(low)} vertices at y {P[low,1].mean():.3f}, torso edge {len(high)} at y {P[high,1].mean():.3f}, {len(fixed)} triangles ({flipped} turned over), {area*1e4:.0f} cm2")
        for k, v in prim['attributes'].items():
            prim['attributes'][k] = add(np.concatenate([A[k], np.array(new[k], A[k].dtype)]), v, 34962)
        index_type = CT[g['accessors'][prim['indices']]['componentType']]
        assert base + len(low) < np.iinfo(index_type).max
        prim['indices'] = add(np.concatenate([I.reshape(-1), np.array(fixed, np.int64).reshape(-1)]).astype(index_type), prim['indices'], 34963)
g['buffers'][0]['byteLength'] = len(buf)
if write:
    json.dump(g, open(path, 'w'), separators=(',', ':')); open(binpath, 'wb').write(buf); print('written')
