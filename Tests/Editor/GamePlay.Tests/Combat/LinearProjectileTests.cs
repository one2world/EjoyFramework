using System;
using System.Numerics;
using EjoyFramework.GamePlay.Combat;
using NUnit.Framework;
namespace EjoyFramework.GamePlay.Tests.Combat
{
    public sealed class LinearProjectileTests
    {
        private static LinearProjectileState Shot(uint tick=1,uint generation=1)=>new LinearProjectileState{
            Key=new ProjectileKey(1,generation,tick,1),Start=Vector3.Zero,End=Vector3.UnitX*200,Position=Vector3.Zero,
            Speed=600,Radius=.2f,MaxAgeSeconds=1,ImpactRetentionSeconds=1,LastTick=tick-1,Mask=2};
        [Test] public void FullSegmentHitsThinMovingHistoricalTargetOnceThenReclaims()
        {
            var history=new HistoricalHitWorld(8,4);history.Reset(5);
            history.Capture(1,new[]{HistoricalCollider.Box(2,3,0,2,new Vector3(5,0,0),new Vector3(.01f,1,1),Quaternion.Identity)});
            history.Capture(2,new[]{HistoricalCollider.Box(2,3,0,2,new Vector3(5,20,0),new Vector3(.01f,1,1),Quaternion.Identity)});
            var world=new LinearProjectileWorld(4);world.Reset(5);var p=Shot();p.CollisionDelay=1;Assert.That(world.Spawn(in p),Is.True);Assert.That(world.Spawn(in p),Is.False);
            var events=new ProjectileImpact[4];var result=world.Advance(2,history,false,events);
            Assert.That(result.Impacts,Is.EqualTo(1));Assert.That(events[0].Hit.Collider.Generation,Is.EqualTo(3));Assert.That(events[0].QueryTick,Is.EqualTo(1));
            Assert.That(world.Advance(3,history,false,events).Impacts,Is.Zero);
            Assert.That(world.Advance(62,history,false,events).Expired,Is.Zero);
            Assert.That(world.Advance(63,history,false,events).Expired,Is.EqualTo(1));Assert.That(world.Count,Is.Zero);
        }
        [Test] public void LastMovementQueryPrecedesAgeDespawn()
        {
            var history=new HistoricalHitWorld(4,4);history.Reset(1);history.Capture(2,new[]{HistoricalCollider.Sphere(2,1,0,2,new Vector3(5,0,0),.1f)});
            var world=new LinearProjectileWorld(4);world.Reset(1);var p=Shot();p.MaxAgeSeconds=.01f;world.Spawn(in p);var events=new ProjectileImpact[4];
            var result=world.Advance(2,history,false,events);Assert.That(result.Impacts,Is.EqualTo(1));Assert.That(result.Expired,Is.Zero);Assert.That(world.Count,Is.EqualTo(1));
        }
        [Test] public void MissingHistoryIsExplicitAndDoesNotSkipUnsweptDistance()
        {
            var history=new HistoricalHitWorld(4,4);history.Reset(1);var world=new LinearProjectileWorld(4);world.Reset(1);var p=Shot();world.Spawn(in p);var events=new ProjectileImpact[4];
            Assert.That(world.Advance(2,history,false,events).MissingHistory,Is.EqualTo(1));
            history.Capture(3,new[]{HistoricalCollider.Sphere(2,1,0,2,new Vector3(5,0,0),.1f)});
            Assert.That(world.Advance(3,history,false,events).Impacts,Is.EqualTo(1));
        }
        [Test] public void ExplicitLatestFallbackAndFlightAgeAreBounded()
        {
            var history=new HistoricalHitWorld(4,4);history.Reset(1);history.Capture(1,Array.Empty<HistoricalCollider>());
            var world=new LinearProjectileWorld(1);world.Reset(1);var p=Shot();world.Spawn(in p);var events=new ProjectileImpact[1];
            var r=world.Advance(2,history,true,events);Assert.That(r.Fallbacks,Is.EqualTo(1));Assert.That(r.MissingHistory,Is.Zero);Assert.That(r.FutureFallbacks,Is.EqualTo(1));Assert.That(r.PastFallbacks,Is.Zero);
            Assert.That(world.Advance(62,history,false,events).Expired,Is.EqualTo(1));
        }
        [Test] public void ExpiredHistoryFallbackHitExposesBothRequestedAndUsedTicks()
        {
            var history=new HistoricalHitWorld(4,4);history.Reset(1);
            history.Capture(10,new[]{HistoricalCollider.Sphere(2,1,0,2,new Vector3(5,0,0),.1f)});
            var world=new LinearProjectileWorld(1);world.Reset(1);var p=Shot();p.CollisionDelay=8;world.Spawn(in p);var events=new ProjectileImpact[1];
            var result=world.Advance(10,history,true,events);
            Assert.That(result.Impacts,Is.EqualTo(1));Assert.That(result.PastFallbacks,Is.EqualTo(1));Assert.That(result.FutureFallbacks,Is.Zero);
            Assert.That(events[0].RequestedQueryTick,Is.EqualTo(2));Assert.That(events[0].QueryTick,Is.EqualTo(10));Assert.That(events[0].UsedLatestHistory,Is.True);
            Assert.That(world.Advance(11,history,true,events).Impacts,Is.Zero);
        }
        [Test] public void OverlapDeduplicatesOwnersAndForceIncludesSelfWithoutIncludingTeam()
        {
            var history=new HistoricalHitWorld(4,8);history.Reset(1);
            history.Capture(1,new[]{HistoricalCollider.Sphere(2,7,1,2,Vector3.Zero,.5f),HistoricalCollider.Capsule(2,7,0,2,Vector3.Zero,Vector3.UnitY,.5f),HistoricalCollider.Box(3,1,0,1,Vector3.Zero,Vector3.One,Quaternion.CreateFromAxisAngle(Vector3.UnitY,.5f)),HistoricalCollider.Sphere(1,1,0,1,Vector3.Zero,.5f)});
            var output=new HistoricalCollider[8];Assert.That(history.OverlapSphere(1,1,Vector3.Zero,1,2,0,1,output,out int count),Is.EqualTo(HistoricalQueryStatus.Hit));
            Assert.That(count,Is.EqualTo(2));Assert.That(output[0].Owner,Is.EqualTo(1));Assert.That(output[1].Owner,Is.EqualTo(2));Assert.That(output[1].ShapeId,Is.Zero);
            Assert.That(history.OverlapSphere(2,1,Vector3.Zero,1,2,0,1,output,out count),Is.EqualTo(HistoricalQueryStatus.WrongEpoch));
            Assert.Throws<ArgumentException>(()=>history.OverlapSphere(1,1,Vector3.Zero,1,2,0,0,new HistoricalCollider[1],out _));
        }
        [Test] public void ReplicaConfirmsExistingViewCorrectsGeometryAndSuppressesDuplicateImpact()
        {
            var replicas=new ProjectileReplicaSet(4);var changes=new ProjectileReplicaTransition[12];replicas.Reset(10,changes);
            var p=Shot();Assert.That(replicas.Predict(10,in p,out _),Is.True);Assert.That(replicas.Predict(10,in p,out _),Is.False);
            p.LastTick=2;p.ImpactTick=2;p.Position=Vector3.UnitX*5;
            Assert.That(replicas.Reconcile(10,2,new[]{p},changes),Is.EqualTo(2));Assert.That(changes[0].Change,Is.EqualTo(ProjectileReplicaChange.Confirmed));Assert.That(changes[1].Change,Is.EqualTo(ProjectileReplicaChange.Impacted));
            Assert.That(replicas.Reconcile(10,2,new[]{p},changes),Is.Zero);p.LastTick=3;Assert.That(replicas.Reconcile(10,3,new[]{p},changes),Is.Zero);
            Assert.That(replicas.Expire(20,5,changes),Is.EqualTo(1));Assert.That(replicas.VisibleCount,Is.Zero);
            p.LastTick=21;int n=replicas.Reconcile(10,21,new[]{p},changes);Assert.That(n,Is.EqualTo(1));Assert.That(changes[0].Change,Is.EqualTo(ProjectileReplicaChange.Appeared));
            Assert.That(replicas.Reconcile(10,22,Array.Empty<LinearProjectileState>(),changes),Is.EqualTo(1));
            var next=Shot(23);replicas.Predict(10,in next,out _);next.End+=Vector3.UnitY;next.LastTick=24;
            replicas.Reconcile(10,24,new[]{next},changes);Assert.That(changes[0].Change,Is.EqualTo(ProjectileReplicaChange.Corrected));
        }
        [Test] public void RejectionTimeoutResetAndLifeRetirementDoNotAcceptOldSession()
        {
            var replicas=new ProjectileReplicaSet(4);var changes=new ProjectileReplicaTransition[12];replicas.Reset(10,changes);var p=Shot();replicas.Predict(10,in p,out _);
            Assert.That(replicas.Reconcile(10,1,Array.Empty<LinearProjectileState>(),changes),Is.Zero);
            Assert.That(replicas.Reconcile(10,2,Array.Empty<LinearProjectileState>(),changes),Is.EqualTo(1));Assert.That(changes[0].Change,Is.EqualTo(ProjectileReplicaChange.Rejected));
            p=Shot(3);replicas.Predict(10,in p,out _);Assert.That(replicas.RetireOwner(1,2,changes),Is.EqualTo(1));
            p=Shot(4,2);replicas.Predict(10,in p,out _);Assert.That(replicas.Reset(11,changes),Is.EqualTo(1));Assert.That(replicas.Reconcile(10,5,new[]{p},changes),Is.EqualTo(-1));Assert.That(replicas.Count,Is.Zero);
        }
        [Test] public void InvalidCompleteFrameCannotPartiallyMutateReplicaSet()
        {
            var replicas=new ProjectileReplicaSet(2);var changes=new ProjectileReplicaTransition[6];replicas.Reset(1,changes);var p=Shot();replicas.Predict(1,in p,out _);
            Assert.Throws<ArgumentException>(()=>replicas.Reconcile(1,2,new[]{p,p},changes));Assert.That(replicas.AppliedTick,Is.Zero);Assert.That(replicas.VisibleCount,Is.EqualTo(1));
        }
        [Test] public void WarmFlightAndOverlapAllocateNoManagedMemory()
        {
            var world=new LinearProjectileWorld(4);var history=new HistoricalHitWorld(4,4);var events=new ProjectileImpact[4];var output=new HistoricalCollider[4];var empty=Array.Empty<HistoricalCollider>();
            for(int i=0;i<10;i++){world.Reset(1);history.Reset(1);history.Capture(1,empty);var p=Shot();world.Spawn(in p);world.Advance(1,history,false,events);history.OverlapSphere(1,1,Vector3.Zero,1,2,0,0,output,out _);}
            long before=GC.GetAllocatedBytesForCurrentThread();
            for(int i=0;i<100;i++){world.Reset(1);history.Reset(1);history.Capture(1,empty);var p=Shot();world.Spawn(in p);world.Advance(1,history,false,events);history.OverlapSphere(1,1,Vector3.Zero,1,2,0,0,output,out _);}
            Assert.That(GC.GetAllocatedBytesForCurrentThread()-before,Is.Zero);
        }

        [TestCase(false)] [TestCase(true)]
        public void FreshOffsetPositionIsRejectedBeforeRegistration(bool ballistic)
        {
            var world=new LinearProjectileWorld(1);world.Reset(1);var p=Shot();
            p.Ballistic=ballistic;p.Position=Vector3.UnitY;
            Assert.Throws<ArgumentException>(()=>world.Spawn(in p));Assert.That(world.Count,Is.Zero);
            p.Position=p.Start;Assert.That(world.Spawn(in p),Is.True);
        }

        [TestCase(1f,false)] [TestCase(-1f,false)]
        [TestCase(1f,true)] [TestCase(-1f,true)]
        public void FlightLeavingCoordinateRangeExpiresBeforePublishingInvalidState(float sign,bool ballistic)
        {
            var history=new HistoricalHitWorld(4,1);history.Reset(1);history.Capture(2,Array.Empty<HistoricalCollider>());
            var world=new LinearProjectileWorld(1);world.Reset(1);var p=Shot();
            p.Start=p.Position=new Vector3(sign*999999,0,0);p.End=new Vector3(sign*1000000,0,0);
            p.Speed=120;p.Ballistic=ballistic;p.Velocity=Vector3.UnitX*(sign*120);world.Spawn(in p);
            var impacts=new ProjectileImpact[1];var result=world.Advance(2,history,false,impacts);
            Assert.That(result.Expired,Is.EqualTo(1));Assert.That(result.Impacts,Is.Zero);
            Assert.That(world.Count,Is.Zero);Assert.That(world.CopyTo(new LinearProjectileState[1]),Is.Zero);
            Assert.DoesNotThrow(()=>world.Advance(3,history,false,impacts));
        }

        [Test] public void OversizedSweepExpiresEvenWhenBothEndpointsAreSupported()
        {
            var history=new HistoricalHitWorld(4,1);history.Reset(1);history.Capture(3,Array.Empty<HistoricalCollider>());
            var world=new LinearProjectileWorld(1,1);world.Reset(1);var p=Shot();
            p.Start=p.Position=new Vector3(-999999,0,0);p.End=new Vector3(999999,0,0);p.Speed=600000;world.Spawn(in p);
            var result=world.Advance(3,history,false,new ProjectileImpact[1]);
            Assert.That(result.Expired,Is.EqualTo(1));Assert.That(world.Count,Is.Zero);
        }

        [Test] public void BallisticAccelerationOutsideValidVelocityRangeExpires()
        {
            var history=new HistoricalHitWorld(4,1);history.Reset(1);history.Capture(2,Array.Empty<HistoricalCollider>());
            var world=new LinearProjectileWorld(1,1000);world.Reset(1);var p=Shot();
            p.Ballistic=true;p.Velocity=new Vector3(0,-999999,0);p.Gravity=1000000;world.Spawn(in p);
            var result=world.Advance(2,history,false,new ProjectileImpact[1]);
            Assert.That(result.Expired,Is.EqualTo(1));Assert.That(world.Count,Is.Zero);
        }

        private sealed class EdgeContactQuery : IHistoricalSweepQuery
        {
            public uint LatestTick=>1;
            public HistoricalQueryStatus Sweep(ulong epoch,uint tick,Vector3 origin,Vector3 direction,float distance,float radius,uint mask,ulong excludedOwner,out HistoricalHit hit)
            {
                hit=HistoricalHit.StaticGeometry(3,1,0,8,0,0,new Vector3(1000000,0,0),Vector3.UnitX);
                return HistoricalQueryStatus.Hit;
            }
        }
        [Test] public void BounceOffsetCannotPublishAnOutOfRangeReplica()
        {
            var world=new LinearProjectileWorld(1);world.Reset(1);var p=Shot();
            p.Start=p.Position=new Vector3(999999.875f,0,0);p.End=new Vector3(1000000,0,0);
            p.Ballistic=true;p.Mask=p.BounceMask=8;world.Spawn(in p);
            var result=world.Advance(1,new EdgeContactQuery(),false,new ProjectileImpact[1]);
            Assert.That(result.Expired,Is.EqualTo(1));Assert.That(result.Impacts,Is.Zero);Assert.That(world.Count,Is.Zero);
        }
    }
}
