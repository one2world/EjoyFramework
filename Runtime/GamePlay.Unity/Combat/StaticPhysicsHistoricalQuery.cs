using System;
using System.Collections.Generic;
using EjoyFramework.GamePlay.Combat;
using UnityEngine;
using UnityEngine.SceneManagement;
using V=System.Numerics.Vector3;
namespace EjoyFramework.GamePlay.Unity.Combat
{
    /// <summary>Composes immutable static Unity mesh geometry with historical dynamic shapes.
    /// Use a dedicated layer containing exactly the supplied static colliders; moving geometry belongs
    /// in history. Checks transforms/enabled state once per captured tick and fails on mutation.
    /// Non-convex meshes are queried as surfaces, not solid-volume interiors. Unknown colliders
    /// are rejected when Unity reports a cast hit or initial overlap, not by scanning all layer occupants.
    /// Zero-radius containment is checked against registered solid primitives and convex meshes.
    /// Initial overlaps on readable non-convex meshes use cached triangles for their contact point;
    /// unreadable meshes report the sweep origin and opposite travel normal (no unique penetration contact).
    /// All buffers are allocated at construction; sweeps do not mutate live physics or allocate.</summary>
    public sealed class StaticPhysicsHistoricalQuery : IHistoricalSweepQuery
    {
        private readonly HistoricalHitWorld history;
        private readonly PhysicsScene scene;
        private readonly Collider[] geometry,overlaps;
        private readonly Matrix4x4[] poses;
        private readonly Vector3[][] meshVertices;
        private readonly int[][] meshTriangles;
        // Keep Unity object identity as the key; shape IDs are explicit registration indices.
        // GetInstanceID is unavailable in Unity 6000.6+, and no EntityId conversion is needed.
        private readonly Dictionary<Collider,ushort> ids;
        private readonly ulong owner;
        private readonly uint mask;
        private readonly int layers;
        private uint validatedTick;
        private ulong validatedEpoch;
        public uint LatestTick=>history.LatestTick;
        public StaticPhysicsHistoricalQuery(HistoricalHitWorld history,PhysicsScene scene,Collider[] staticGeometry,int layerMask,ulong owner,uint categoryMask)
        {
            this.history=history??throw new ArgumentNullException(nameof(history));
            if(!scene.IsValid()||staticGeometry==null||staticGeometry.Length==0||staticGeometry.Length>4096||layerMask==0||owner==0||categoryMask==0)throw new ArgumentException("Bounded static geometry and explicit layer/identity required.");
            // Registration and pose checks establish runtime immutability. GameObject.isStatic is a bake/editor flag, not a player contract.
            this.scene=scene;this.owner=owner;mask=categoryMask;layers=layerMask;
            geometry=(Collider[])staticGeometry.Clone();overlaps=new Collider[geometry.Length+1];poses=new Matrix4x4[geometry.Length];ids=new Dictionary<Collider,ushort>(geometry.Length);
            meshVertices=new Vector3[geometry.Length][];meshTriangles=new int[geometry.Length][];
            for(int i=0;i<geometry.Length;i++)
            {
                var c=geometry[i];
                if(!c||!c.enabled||!c.gameObject.activeInHierarchy||c.isTrigger||(layers&(1<<c.gameObject.layer))==0||c.gameObject.scene.GetPhysicsScene()!=scene)throw new ArgumentException("Geometry must be enabled, non-trigger and in the specified scene/layer.");
                ids.Add(c,(ushort)i);poses[i]=c.transform.localToWorldMatrix;
                if(c is MeshCollider mesh&&!mesh.convex&&mesh.sharedMesh&&mesh.sharedMesh.isReadable)
                {
                    var vertices=mesh.sharedMesh.vertices;
                    for(int v=0;v<vertices.Length;v++)vertices[v]=poses[i].MultiplyPoint3x4(vertices[v]);
                    meshVertices[i]=vertices;meshTriangles[i]=mesh.sharedMesh.triangles;
                }
            }
        }
        public HistoricalQueryStatus Sweep(ulong epoch,uint tick,V origin,V direction,float distance,float radius,uint queryMask,ulong excludedOwner,out HistoricalHit hit)
        {
            var result=history.Sweep(epoch,tick,origin,direction,distance,radius,queryMask,excludedOwner,out hit);
            if(result==HistoricalQueryStatus.WrongEpoch||result==HistoricalQueryStatus.HistoryUnavailable)return result;
            if(validatedEpoch!=epoch||validatedTick!=history.LatestTick)
            {
                for(int i=0;i<geometry.Length;i++)if(!geometry[i]||!geometry[i].enabled||!geometry[i].gameObject.activeInHierarchy||geometry[i].isTrigger||(layers&(1<<geometry[i].gameObject.layer))==0||geometry[i].transform.localToWorldMatrix!=poses[i])throw new InvalidOperationException("Static collision geometry changed; rebuild the scene query explicitly.");
                validatedEpoch=epoch;validatedTick=history.LatestTick;
            }
            if((queryMask&mask)==0||excludedOwner==owner)return result;
            var start=U(origin);var dir=U(V.Normalize(direction));
            // Unity casts omit initial overlaps. Resolve these explicitly and reject unknown layer occupants.
            if(radius==0)
            {
                // A point has no volume for OverlapSphere. Test exact containment without an
                // epsilon sphere that could turn a nearby surface into a false initial hit.
                for(int i=0;i<geometry.Length;i++)
                {
                    var c=geometry[i];
                    if(!(c is BoxCollider)&&!(c is SphereCollider)&&!(c is CapsuleCollider)&&!(c is MeshCollider mesh&&mesh.convex))continue;
                    var closest=c.ClosestPoint(start);
                    if(closest.x==start.x&&closest.y==start.y&&closest.z==start.z)
                    {hit=HistoricalHit.StaticGeometry(owner,1,(ushort)i,mask,0,0,N(start),N(-dir));return HistoricalQueryStatus.Hit;}
                }
            }
            if(radius>0)
            {
                int count=scene.OverlapSphere(start,radius,overlaps,layers,QueryTriggerInteraction.Ignore);
                if(count==overlaps.Length)throw new InvalidOperationException("Static overlap budget exhausted.");
                if(count>0)
                {
                    ushort best=ushort.MaxValue;Collider selected=null;
                    for(int i=0;i<count;i++){ushort id=Identity(overlaps[i]);if(id<best){best=id;selected=overlaps[i];}}
                    var point=ClosestPoint(best,selected,start);var normal=start-point;if(normal.sqrMagnitude<.000001f)normal=-dir;
                    hit=HistoricalHit.StaticGeometry(owner,1,best,mask,0,0,N(point),N(normal));return HistoricalQueryStatus.Hit;
                }
            }
            RaycastHit staticHit;
            bool found=radius>0?scene.SphereCast(start,radius,dir,out staticHit,distance,layers,QueryTriggerInteraction.Ignore):scene.Raycast(start,dir,out staticHit,distance,layers,QueryTriggerInteraction.Ignore);
            if(found)
            {
                ushort id=Identity(staticHit.collider);
                if(result!=HistoricalQueryStatus.Hit||staticHit.distance<hit.Distance)
                {hit=HistoricalHit.StaticGeometry(owner,1,id,mask,0,staticHit.distance,N(staticHit.point),N(staticHit.normal));return HistoricalQueryStatus.Hit;}
            }
            return result;
        }
        private ushort Identity(Collider c)=>ids.TryGetValue(c,out var id)?id:throw new InvalidOperationException("Unexpected collider in the static query layer.");
        private Vector3 ClosestPoint(ushort id,Collider collider,Vector3 point)
        {
            var vertices=meshVertices[id];
            if(vertices==null)return collider is MeshCollider mesh&&!mesh.convex?point:collider.ClosestPoint(point);
            var triangles=meshTriangles[id];var best=point;float distance=float.PositiveInfinity;
            for(int i=0;i<triangles.Length;i+=3)
            {
                if(Vector3.Cross(vertices[triangles[i+1]]-vertices[triangles[i]],vertices[triangles[i+2]]-vertices[triangles[i]]).sqrMagnitude==0)continue;
                var candidate=ClosestTrianglePoint(point,vertices[triangles[i]],vertices[triangles[i+1]],vertices[triangles[i+2]]);
                float squared=(point-candidate).sqrMagnitude;
                if(squared<distance){distance=squared;best=candidate;}
            }
            return best;
        }
        private static Vector3 ClosestTrianglePoint(Vector3 p,Vector3 a,Vector3 b,Vector3 c)
        {
            var ab=b-a;var ac=c-a;var ap=p-a;
            float d1=Vector3.Dot(ab,ap),d2=Vector3.Dot(ac,ap);
            if(d1<=0&&d2<=0)return a;
            var bp=p-b;float d3=Vector3.Dot(ab,bp),d4=Vector3.Dot(ac,bp);
            if(d3>=0&&d4<=d3)return b;
            float vc=d1*d4-d3*d2;
            if(vc<=0&&d1>=0&&d3<=0)return a+ab*(d1/(d1-d3));
            var cp=p-c;float d5=Vector3.Dot(ab,cp),d6=Vector3.Dot(ac,cp);
            if(d6>=0&&d5<=d6)return c;
            float vb=d5*d2-d1*d6;
            if(vb<=0&&d2>=0&&d6<=0)return a+ac*(d2/(d2-d6));
            float va=d3*d6-d5*d4;
            if(va<=0&&(d4-d3)>=0&&(d5-d6)>=0)return b+(c-b)*((d4-d3)/((d4-d3)+(d5-d6)));
            float inverse=1/(va+vb+vc);
            return a+ab*(vb*inverse)+ac*(vc*inverse);
        }
        private static Vector3 U(V value)=>new Vector3(value.X,value.Y,value.Z);
        private static V N(Vector3 value)=>new V(value.x,value.y,value.z);
    }
}
