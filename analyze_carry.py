import sys

log = "collision_frame_log.txt"
frame = ""
prev = {}
carry_events = []

with open(log, "r", encoding="utf-8") as f:
    for line in f:
        line = line.rstrip("\n")
        if line.startswith("FRAME\t"):
            parts = line.split("\t")
            frame = parts[1] if len(parts) > 1 else ""
        elif line.startswith("OBJECT\t"):
            parts = line.split("\t")
            d = {}
            for i in range(0, len(parts) - 1, 2):
                d[parts[i]] = parts[i + 1]
            obj_id = d.get("ID", "")
            name   = d.get("NAME", "")
            pos    = d.get("POS", "")
            dist   = d.get("DIST_PLAYER", "")
            rel    = d.get("REL_POS", "")
            relrot = d.get("REL_ROT", "")
            car    = d.get("IS_CARRIED", "0")
            p = prev.get(obj_id, {"car": "0", "rel": "", "dist": "", "pos": ""})
            if car == "1" and p["car"] != "1":
                carry_events.append({
                    "frame":       frame,
                    "name":        name,
                    "id":          obj_id,
                    "prev_dist":   p["dist"],
                    "prev_rel":    p["rel"],
                    "prev_pos":    p["pos"],
                    "start_dist":  dist,
                    "start_rel":   rel,
                    "start_relrot":relrot,
                    "start_pos":   pos,
                })
            prev[obj_id] = {"car": car, "rel": rel, "dist": dist, "pos": pos}

if not carry_events:
    print("No carry start events found in log.")
else:
    for e in carry_events:
        print(f"=== CARRY START  frame={e['frame']}  obj={e['name']}  id={e['id']}")
        print(f"  BEFORE  dist={e['prev_dist']}  rel={e['prev_rel']}  pos={e['prev_pos']}")
        print(f"  START   dist={e['start_dist']}  rel={e['start_rel']}  relrot={e['start_relrot']}  pos={e['start_pos']}")
        print()
