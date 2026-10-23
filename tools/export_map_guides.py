"""Build offline, source-labelled map guides without inventing unknown routes or priorities."""
import argparse
import hashlib
import json
import re
from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]

def build(config_bundle=None):
    catalog = json.loads((ROOT/'AstralParty-Hub/src/shared/gameCatalog.generated.json').read_text('utf-8-sig'))
    guide_path = ROOT/'XingShu-Dev/build/catalog/guide-tables.json'
    required = {'PVEMission','STRPVEMission','Monster','STRMonster','MapEvent','STRMapEvent','Skill','STRSkill'}
    if config_bundle:
        import UnityPy
        from export_game_catalog import SCHEMA, decode
        config_bundle = Path(config_bundle)
        if hashlib.sha256(config_bundle.read_bytes()).hexdigest() != catalog['configSha256']:
            raise ValueError('Configuration hash differs from the catalog; do not mix platform/version data.')
        guide = {'sourceSha256':catalog['configSha256']}
        for obj in UnityPy.load(str(config_bundle)).objects:
            if obj.type.name != 'TextAsset': continue
            asset = obj.read(); typ = asset.m_Name+'Configure'
            if asset.m_Name in required and typ in SCHEMA:
                guide[asset.m_Name] = decode(asset.m_Script.encode('utf-8','surrogateescape'), typ)
        guide_path.parent.mkdir(parents=True, exist_ok=True)
        guide_path.write_text(json.dumps(guide,ensure_ascii=False,indent=2),'utf-8')
    else:
        guide = json.loads(guide_path.read_text('utf-8')) if guide_path.exists() else {}
    if guide.get('sourceSha256') != catalog['configSha256']:
        raise ValueError('Supply --config-bundle with the original catalog configuration to export verified mission data.')
    if not required.issubset(guide):
        raise ValueError('Verified configuration is missing guide tables: '+', '.join(sorted(required-set(guide))))
    def rows(table, name): return guide[table].get(name, [])
    loc={k:{r['Id']:r.get('Simplified','') for r in rows(k,'Locals')} for k in ('STRMapEvent','STRMonster','STRSkill','STRPVEMission')}
    def text(table, key): return loc.get('STR'+table,{}).get(key,'')
    monsters={r['Id']:r for r in rows('Monster','Infos')}
    events={r['Id']:r for r in rows('MapEvent','MapEventCards')}
    event_rules={r['Id']:r for r in rows('MapEvent','Infos')}
    skills={r['Id']:r for r in rows('Skill','Infos')}
    missions={r['Id']:r for r in rows('PVEMission','Infos')}
    def mission(eid):
        r=missions.get(eid,{})
        desc=text('PVEMission',r.get('MissionDescID'))
        count=None; names=[]
        config=r.get('MissionTargetConfigs',{})
        kill=config.get('1',{}).get('Values',[])
        if kill: count=kill[0]; names=[text('Monster',monsters.get(i,{}).get('NameID')) or f'怪物 #{i}' for i in kill[1:]]
        elif desc.startswith('击倒') and r.get('MissionTarget'):
            names=[text('Monster',monsters.get(i,{}).get('NameID')) or f'怪物 #{i}' for i in r['MissionTarget']]
            count=next(iter(r.get('MissionParam',[])),None)
        if names:
            title='击倒 '+(' / '.join(names))+ (f' ×{count}' if count is not None else '')
            if '和' in desc: title='分别击倒 '+('、'.join(names))+ (f' 各 ×{count}' if count is not None else '')
        elif '8' in config and config['8'].get('Values'): title=desc.replace('{0}',str(config['8']['Values'][0]))
        elif desc.startswith(('获得{0}','收集{0}','苍鲤')) and r.get('MissionParam'): title=desc.replace('{0}',str(r['MissionParam'][0]))
        else: title=desc
        unresolved=bool(re.search(r'\{\d+\}',title))
        if unresolved: title=f'特殊任务 #{eid}（条件待解析）'
        return dict(id=eid,name=title or f'任务 #{eid}',description=desc,reward=text('PVEMission',r.get('RewardDescID')),
            failure=text('PVEMission',r.get('FailedDescID')),count=count,targetNames=names,rawConfig=r,conditionResolved=not unresolved,source='official-config')
    maps=[]
    for m in catalog['maps']:
        if not m.get('name') or not m.get('image'): continue
        ds=[]
        for d in m.get('difficulties',[]):
            pool=[]
            for v in d['monsters']:
                monster=monsters.get(v['id'],{})
                pool.append(dict(id=v['id'],name=v['name'],image=monster.get('CharacterMap',''),
                    mechanism=text('Monster',monster.get('MechanismID')),biography=text('Monster',monster.get('BiographyID')),
                    priority=None,baseHp=monster.get('Blood',0),baseAttack=monster.get('Attack',0),baseDefense=monster.get('Defense',0),
                    skills=[dict(id=s,name=text('Skill',skills.get(s,{}).get('NameID')),description=text('Skill',skills.get(s,{}).get('DescID'))) for s in monster.get('PassiveSkills',[])]))
            es=[]
            for eid in d['eventIds']:
                event=events.get(eid,{})
                es.append(dict(id=eid,name=text('MapEvent',event.get('NameID')),description=text('MapEvent',event.get('DescId')),
                    image=event.get('Image',''),triggerParams=event_rules.get(eid,{}).get('Triggerparams',[])))
            ds.append(dict(index=d['index'],name=d['name'],description=d['description'],progressLimit=d['turnLimit'],
                monsters=pool,events=es,missionIds=d['missionIds'],missions=[mission(i) for i in d['missionIds']],source='official-config'))
        maps.append(dict(id=m['id'],name=m['name'],description=m['description'],image=m['image'],sceneImage=m.get('sceneImage',''),
            source='official-config',difficulties=ds,specialCardIds=m.get('specialCardIds',[]),specialChipIds=m.get('specialChipIds',[]),
            routeStatus='pending-verification',routeNotes=[],community=None))
    forest=next(m for m in maps if m['id']==82017)
    forest['community']={
        'source':'用户提供的森秋幻境攻略图','version':'3.3.0','asOf':'2026-10-16',
        'difficultyIndex':3,'status':'community-reference','title':'森秋幻境 · 疯狂难度实战速查',
        'notes':['以下路线、概率与任务细节来自用户提供的攻略图，尚未完成独立实战复核。','进度数字代表团队进度，不是轮数。攻略提到：整轮所有怪物行动结束后统一结算全队任务。','配置对照：当前原始配置中，猫头鹰任务额外奖励为随机植物建筑卡，与参考图的“御灵卡”文字不同；请以下方配置任务为准，实战差异待核对。'],
        'actionOrder':[{'name':n,'priority':p} for n,p in [('小偷',10),('采集小熊',20),('闹钟猫头鹰',30),('玩具恐龙',60),('孩子王嘎呜',70)]],
        'timeline':[{'progress':p,'name':n,'description':d} for p,n,d in [
            (1,'冒险开始！','玩具恐龙 ×1、采集小熊 ×3；参考图标出固定位置。'),
            (3,'自由自在','嘎呜、小熊、猫头鹰各 ×1；具体落点需结合路线图核对。'),
            (7,'潜伏','采集小熊 ×2、玩具恐龙 ×1；全体怪物 +1 攻击、+1 防御。'),
            (10,'游玩时光','采集小熊 ×2、玩具恐龙 ×2；全体怪物 +1 攻击、+1 防御。')]],
        'buildings':[{'name':n,'chance':p,'effect':d} for n,p,d in [('疾行菇',30,'停留：下次移动 +3'),('仙品杨桃',30,'停留：生命上限 +1'),('芳香蜜桃',20,'经过：回复 2 点生命；使怪物失去关注'),('招财树',20,'轮初：5 格内 +3 星币')]],
        'missions':[{'name':n,'reward':r,'rarities':q} for n,r,q in [
            ('击倒闹钟猫头鹰 ×3','全员获得强化筹码 ×1；另获随机御灵卡 ×1',[60,37,3]),
            ('击倒采集小熊 ×3','全员获得强化筹码 ×1',[40,45,15]),
            ('击倒玩具恐龙 ×2','全员获得强化筹码 ×1；进度 −1',[40,45,15]),
            ('建造 8 个建筑','全员获得强化筹码 ×1',[25,50,25])]],
        'summoning':['怪物格：小偷 30%，猫头鹰 70%；生成在角色停留的格子。','嘎呜召唤：随机选择一个相邻格。','新召唤怪物追加至本轮队尾，本轮可以行动。'],
        'routes':['攻略图给出保底点 49、44、42、35，依次标作 1～4；这些是参考图标记，尚未与游戏格子 ID 对齐。','路口起步：有红走红，无红走蓝；路中遇怪时另按图示限制处理。','建造任务累计达到 8 个建筑，兼顾治疗格与事件格。']}
    result={'version':1,'gameVersion':catalog['version'],'configSha256':catalog['configSha256'],'maps':maps}
    target=ROOT/'AstralParty-Hub/src/shared/mapGuides.generated.json'
    target.write_text(json.dumps(result,ensure_ascii=False,indent=2)+'\n','utf-8')
    print(f'Guides: {len(maps)} maps, {sum(len(m["difficulties"]) for m in maps)} difficulty profiles; community sources labelled separately.')

if __name__=='__main__':
    parser=argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--config-bundle',type=Path,help='Original unmodified Unity configuration matching the catalog SHA256')
    build(parser.parse_args().config_bundle)
