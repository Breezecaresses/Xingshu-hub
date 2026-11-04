import type {LibraryAccount,PlayedMatch} from './gameLibrary';

const cnPlatforms=new Set(['STEAM_CN','STEAM','TAPTAP','BILIBILI','WEGAME','PHONE_CN']);
export function cloudMatchesAccount(uid:string|undefined,account:Pick<LibraryAccount,'platform'|'snapshot'>|undefined){
  return !!uid&&!!account&&cnPlatforms.has(account.platform)&&String(account.snapshot.playerId)===uid;
}
const matchKey=(record:PlayedMatch)=>`${record.time}:${record.heroId}:${record.mapId}`;
/** Verified same game UID, CN endpoint only. Keep local role bindings and older saved history. */
export function mergeCloudLibrary(local:LibraryAccount[],cloud:LibraryAccount|null|undefined):LibraryAccount[]{
  if(!cloud)return local;
  let linked=false;
  const merged=local.map(account=>{
    if(!cloudMatchesAccount(String(cloud.snapshot.playerId),account))return account;
    linked=true;
    const cloudNewer=Date.parse(cloud.observedAt)>=Date.parse(account.observedAt);
    const recent=cloudNewer?cloud:account,older=cloudNewer?account:cloud;
    const records=new Map<string,PlayedMatch>();
    for(const record of [...older.snapshot.matches||[],...recent.snapshot.matches||[]]){
      const key=matchKey(record),previous=records.get(key);
      records.set(key,{...record,detailAvailable:!!record.detailAvailable||!!previous?.detailAvailable});
    }
    return {...account,observedAt:recent.observedAt,source:'combined_official_account',official:cloud.official,
      snapshot:{...recent.snapshot,matches:[...records.values()].sort((a,b)=>b.time-a.time).slice(0,1000)}};
  });
  return linked?merged:[...merged,cloud];
}
